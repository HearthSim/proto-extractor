using protoextractor.IR;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace protoextractor.processing
{
	// Care casing!
	// Namespace names are cased in lowercase.
	// Class types are cased in PascalCase.
	// Enum types are cased in PascalCase.
	//      Class Property names are snake_cased.
	//      Enum property names are in UPPERCASE.
	// This processor depends on the fact that the original name of types is ALWAYS UNIQUE!
	class NameCollisionAnalyzer : DefaultProcessor
	{
		// Object initialised to hash bytes.
		private MD5 _md5Hash;

		public NameCollisionAnalyzer(IRProgram program) : base(program)
		{
			_md5Hash = MD5.Create();
		}

		public override IRProgram Process()
		{
			Program.Log.OpenBlock("NameCollisionAnalyzer::Process()");

			// Test for name collisions of types within the scope of the parent type.
			CollidingPrivateTypeNames();

			// Test for name collisions of types within the scope of the parent namespace.
			CollidingPublicTypeNames();

			// Test public enums properties and solve collisions.
			CollidingPublicEnumProperties();

			// Test private enum properties and solve collisions.
			CollidingPrivateEnumProperties();

			// Test namespace components and namespace types to avoid exact names equal to
			// a namespace and a certain other type(s).
			CollidingPublicTypesInterNamespace();

			Program.Log.CloseBlock();
			return _program;
		}

		// Iterates all namespaces to recursively solve name collisions within private type collections.
		private void CollidingPrivateTypeNames()
		{
			foreach (var ns in _program.Namespaces)
			{
				CollidingPrivateTypeNamesScoped(ns);
			}
		}

		// Renames colliding names of private types, within the scope of the parent type.
		private void CollidingPrivateTypeNamesScoped(IRProgramNode parent)
		{
			// All classes that have private types.
			IEnumerable<IRClass> classesWithPrivates = null;

			if (parent is IRNamespace)
			{
				// With the namespace as parent, multiple classes could match this query.
				classesWithPrivates = ((IRNamespace)parent).Classes.Where(c => c.PrivateTypes.Count > 0);
			}
			else if (parent is IRClass)
			{
				var irClass = (IRClass)parent;

				if (irClass.PrivateTypes.Count == 0)
				{
					// Exit because this parent has no private types!
					return;
				}
				else
				{
					classesWithPrivates = new List<IRClass> { irClass };
				}
			}
			else
			{
				throw new System.Exception("IR parent type is unknown!");
			}


			foreach (var collisionScopeClass in classesWithPrivates)
			{
				// Find all private types which collide.
				// Private types are IRClass and/or IREnum.
				// These name collisions are automatically scoped within the parent.
				var collisions = collisionScopeClass.PrivateTypes.GroupBy(x => x.ShortName)
								 .Where(group => group.Count() > 1)
								 .Select(group => group.Key);
				foreach (var collision in collisions)
				{
					// Collect all types that collide.
					var typeList = collisionScopeClass.PrivateTypes.Where(t => t.ShortName.Equals(collision));
					RenameTypes(typeList);
				}

				// Recurse into each private class.
				foreach (var privType in collisionScopeClass.PrivateTypes)
				{
					if (privType is IRClass)
					{
						CollidingPrivateTypeNamesScoped(privType);
					}
				}
			}
		}

		// Solves colliding enum keys within the scope of the parent namespace.
		// Only public enums are scanned!
		private void CollidingPublicEnumProperties()
		{
			// Public enums have there properties scoped by the parent namespace.
			// To avoid enum property collisions, we must check collisions accross all
			// property names per namespace.
			foreach (var ns in _program.Namespaces)
			{
				// Fetch all properties of public enums.
				var props = ns.Enums.Where(e => e.IsPrivate == false).SelectMany(e => e.Properties);
				// Get list of collided property names.
				var collisions = props.GroupBy(e => e.Name)
								 .Where(group => group.Count() > 1)
								 .Select(group => group.Key);

				foreach (var collision in collisions)
				{
					// Get all properties of enums for the current namespace matching
					// the collision as name.
					var renameProperties = props.Where(p => p.Name.Equals(collision));
					RenameProperties(renameProperties);
				}
			}
		}

		// Solves colliding enums key names within the scope of the parent type.
		// The scope is narrowed down because the parent type acts as a namespace.
		private void CollidingPrivateEnumProperties()
		{
			// Fetch all classes from each namespace, with private types.
			var targetClasses = _program.Namespaces.SelectMany(n => n.Classes).Where(
									c => c.PrivateTypes.Count > 0);

			foreach (var irClass in targetClasses)
			{
				// We are only interested in the private enums.
				// Enum properties are actually contained by the parent container (type or namespace) [in some languages]
				var privateEnums = irClass.PrivateTypes.Where(t => (t is IREnum)).Cast<IREnum>();
				// Fetch all properties of these enums.
				var props = privateEnums.SelectMany(e => e.Properties);
				// Get list of colliding names.
				var collisions = props.GroupBy(e => e.Name)
								 .Where(group => group.Count() > 1)
								 .Select(group => group.Key);

				foreach (var collision in collisions)
				{
					// Fetch all properties that collide with this exact name.
					var renameProps = props.Where(p => p.Name.Equals(collision));
					RenameProperties(renameProps);
				}
			}

		}

		// Test for shortname collisions within one namespace.
		// This does NOT test for nested type collisions!
		private void CollidingPublicTypeNames()
		{
			foreach (var ns in _program.Namespaces)
			{
				// Throw all names of types in one list.
				// Only select the public types, because we currently don't care about private/nested types.
				List<string> allShortNames = new List<string>();
				var classEnumeration = ns.Classes.Where(c => c.IsPrivate == false).Select(c => c.ShortName);
				var enumEnumeration = ns.Enums.Where(e => e.IsPrivate == false).Select(e => e.ShortName);

				allShortNames.AddRange(classEnumeration);
				allShortNames.AddRange(enumEnumeration);

				// Generate a set of unique elements from the collection.
				// If the amount of elements doesn't match, there is a name collision.
				var distinctSet = allShortNames.Distinct();
				if (distinctSet.Count() != allShortNames.Count())
				{
					// Solve the name collision(s)..
					SolveCollisionsWithin(ns, allShortNames);
				}
			}
		}

		// Solves name collisions between types in one namespace.
		private void SolveCollisionsWithin(IRNamespace ns, List<string> allShortNames)
		{
			// Find all types which collide.
			var collisions = allShortNames.GroupBy(x => x)
							 .Where(group => group.Count() > 1)
							 .Select(group => group.Key);
			foreach (var collisionName in collisions)
			{
				// Find all types matching the collision name.
				// NO case mismatch!
				var classesEnumeration = ns.Classes.Where(c => c.ShortName.Equals(collisionName));
				var enumEnumeration = ns.Enums.Where(e => e.ShortName.Equals(collisionName));

				// Throw them together in one list.
				List<IRTypeNode> collidedTypes = new List<IRTypeNode>();
				collidedTypes.AddRange(classesEnumeration);
				collidedTypes.AddRange(enumEnumeration);

				// Rename collided types.
				RenameTypes(collidedTypes);
			}

		}

		private void CollidingPublicTypesInterNamespace()
		{
			// This flag tells string.equals() to ignore casing.
			var strComparisonFlag = System.StringComparison.OrdinalIgnoreCase;
			// Boolean to keep track of all collisions being solved.
			var solved = false;

			// All namespaces to process.
			var processNamespaces = _program.Namespaces.ToList();

			while (!solved)
			{
				// Start with assumption that all collisions are solved.
				solved = true;

				// For each namespace, find all parent namespaces.
				// Check if the parent namespaces doesn't have a type that is named
				// after one of it's child namespaces.
				// Work on a copy because we manipulate the list during iteration.
				foreach (var ns in processNamespaces.ToList())
				{
					// Find parent namespaces.
					var parents = NameCollisionHelper.FindNSParents(_program, ns);
					foreach (var parentNS in parents)
					{
						// Subtract the parent namespace string from the current namespace name.
						// What's left is a shortname or multiple namespace name pieces.
						var parentNSNameCount = parentNS.FullName.Count();
						var childName = ns.FullName.Substring(parentNSNameCount).Trim('.');

						// Check if there is any type within the child namespace that has a shortname
						// equal to the residue part of the namespace subtraction.
						// Care for case mismatch! PascalCase <-> lowercase
						var foundClasses = parentNS.Classes.Where(c => c.ShortName.Equals(childName,
																  strComparisonFlag));
						var foundEnums = parentNS.Enums.Where(e => e.ShortName.Equals(childName,
															  strComparisonFlag));

						// It could be that multiple types share names, but we ignore that fact here.
						// Other methods solve name collisions within one namespace.
						if (foundClasses.Any() || foundEnums.Any())
						{
							// Rename child namespace.
							RenameNamespace(ns);
							// Go for another try on all namespaces.
							solved = false;
							break;
						}
					}

					if (solved != true)
					{
						// Loop again over all namespaces including this one.
						break;
					}

					// Remove this namespace from the list.
					processNamespaces.Remove(ns);
				}
			}
		}

		private void RenameNamespace(IRNamespace ns)
		{
			Program.Log.Info("Renaming namespace `{0}`", ns.OriginalName);

			// Use the original name to generate a hash
			// and append it to the namespace.
			var nsHash = GetMD5Hash(ns.OriginalName);
			// Generate affix
			var affix = string.Format("_{0}", nsHash.Substring(0, 3));

			// Append affix to both shortname and fullname.
			ns.ShortName = ns.ShortName + affix;
			ns.FullName = ns.FullName + affix;

			// Update all the types of this namespace.
			foreach (var irClass in ns.Classes)
			{
				irClass.UpdateTypeName(ns);
			}

			foreach (var irEnum in ns.Enums)
			{
				irEnum.UpdateTypeName(ns);
			}
		}

		private void RenameTypes(IEnumerable<IRTypeNode> types)
		{
			foreach (var type in types)
			{
				Program.Log.Info("Renaming type `{0}`", type.OriginalName);

				// Prepare affix, this has to be somewhat deterministic so we use the hash
				// of the original fullname.
				var typeHash = GetMD5Hash(type.OriginalName);
				// Take first 3 characters from the hash.
				typeHash = typeHash.Substring(0, 3);

				var affix = string.Format("_a{0}", typeHash);
				// Append affix to full and shortname.
				type.FullName = type.FullName + affix;
				type.ShortName = type.ShortName + affix;
			}
		}

		private void RenameProperties(IEnumerable<IREnumProperty> properties)
		{
			foreach (var prop in properties)
			{
				// Prepare affix, this has to be somewhat deterministic, so resolve fullName of the
				// parent enum (which we have to find :/)
				var parentEnum = _program.Namespaces.SelectMany(ns => ns.Enums)
								 .First(e => e.Properties.Contains(prop));

				Program.Log.Info("Renaming property `{0}` or enum", prop.Name, parentEnum.OriginalName);

				// Use the parent enum original name to construct our hash.
				var typeHash = GetMD5Hash(parentEnum.OriginalName);
				// Take first 3 characters from the hash.
				typeHash = typeHash.Substring(0, 3);

				var affix = string.Format("_a{0}", typeHash);
				// Append affix to full and shortname.
				prop.Name = prop.Name + affix;
			}
		}

		private string GetMD5Hash(string input)
		{
			// Example taken from: https://msdn.microsoft.com/en-us/library/s02tk69a(v=vs.110).aspx
			// Construct hash.
			byte[] data = _md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
			// Generate hexadecimal representation of hash.
			StringBuilder builder = new StringBuilder();
			foreach (var b in data)
			{
				builder.Append(b.ToString("x2"));
			}
			// Return hex string.
			return builder.ToString();
		}
	}

	public static class NameCollisionHelper
	{
		public static List<IRNamespace> FindNSParents(IRProgram program, IRNamespace subject)
		{
			// Return all namespaces whos fullname are found at the beginnen of the subject
			// namespace.
			var subjName = subject.FullName;
			var parents = program.Namespaces.Where(p => subjName.StartsWith(p.FullName)).ToList();
			// Remove subject, because fullname matches always with itself.
			parents.Remove(subject);
			return parents;
		}
	}
}
