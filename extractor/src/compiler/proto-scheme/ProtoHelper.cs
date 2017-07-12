using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace protoextractor.compiler.proto_scheme
{
	public static class ProtoHelper
	{
		// Returns true if the provided enum uses aliases inside the body.
		public static bool HasEnumAlias(IREnum enumType)
		{
			var propertyValues = enumType.Properties.Select(x => x.Value);
			var propertyLength = propertyValues.Count();
			var distinctPropertyLength = propertyValues.Distinct().Count();

			// TRUE if distinct length is different from normal length.
			return propertyLength != distinctPropertyLength;
		}

		// Converts given namespace objects into paths.
		// The returned string is a relative path to the set '_path' property!
		public static Dictionary<IRNamespace, string> NamespacesToFileNames(
			List<IRNamespace> nsList, bool structured)
		{
			Dictionary<IRNamespace, string> returnValue = new Dictionary<IRNamespace, string>();

			if (structured == true)
			{
				// Do something fancy to figure out folder names.
				foreach (var ns in nsList)
				{
					var nsName = ns.FullName;
					// Split namespace name to generate hierarchy.
					// The last piece is the actual filename of the namespace.
					var pathPieces = nsName.Split('.').ToList();

					// Use the shortname as actual filename.
					pathPieces.Add(ns.ShortName + ".proto");

					// Combine all pieces into a valid path structure.
					// And append the proto file extension.
					var path = Path.Combine(pathPieces.ToArray());
					// Always lowercase paths!
					returnValue.Add(ns, path.ToLower());
				}
			}
			else
			{
				foreach (var ns in nsList)
				{
					returnValue.Add(ns, ns.FullName.ToLower() + ".proto");
				}
			}

			return returnValue;
		}

		// This function converts string in PascalCase to snake_case.
		// eg; BatlleNet => battle_net
		public static string PascalToSnake(this string s)
		{
			IEnumerable<string> chars = s.Select((c, i) =>
			{
				// Create underscore for each encountered Uppercase character.
				// Except when the previous character in original string is also Uppercase.
				char prevChar = (i == 0) ? 'a' : s.ElementAt(i - 1);
				return (Char.IsUpper(c) && !Char.IsUpper(prevChar)) ? ("_" + c.ToString()) : c.ToString();
			});
			return String.Concat(chars).Trim('_').ToLower();
		}

		// Converts a namespace to a package string.
		// The package string is based on the location of the actual file.
		// The package string equals the (relative) path to the folder containing files that
		// share the same package as parent.
		// eg: pkg_parent.pkg_child => <rel path>/pkg_parent/pkg_child/some_file.proto
		public static string ResolvePackageName(IRNamespace ns)
		{
			return ns.FullName.ToLower();
		}

		public static string ResolveTypeReferenceString(IRClass current, IRTypeNode reference)
		{
			var returnValue = "";
			List<IRProgramNode> currentTypeChain; // We are not interested in this one.
			List<IRProgramNode> referenceTypeChain;

			// Check the parent namespace of both types.
			var curNS = GetNamespaceForType(current, out currentTypeChain);
			var refNS = GetNamespaceForType(reference, out referenceTypeChain);
			// Remove the namespace element so it does not interfere with dynamic string building.
			referenceTypeChain.RemoveAt(0);

			// If the namespaces match, a reference to another namespace (=package) must not be made.
			if (curNS != refNS)
			{
				// Update returnValue with the full package name of the namespace.
				returnValue += ResolvePackageName(refNS) + ".";
			}

			// If current type occurs within the referenceType chain, update the chain
			// so it references relative to that type.
			if (referenceTypeChain.Contains(current))
			{
				var removeIdx = referenceTypeChain.IndexOf(current);
				referenceTypeChain.RemoveRange(0, removeIdx + 1);
			}

			// Dynamically construct a path to the referenced type.
			// The chain does NOT include the referenced type itself!
			foreach (var type in referenceTypeChain)
			{
				returnValue += type.ShortName + ".";
			}
			// Finish with the name of the referenced type itself.
			return returnValue + reference.ShortName;
		}

		// Returns the namespace object for the given object.
		public static IRNamespace GetNamespaceForType(IRTypeNode type,
													  out List<IRProgramNode> parentChain)
		{
			// Keep track of the path towards the parent namespace.
			parentChain = new List<IRProgramNode>();
			IRNamespace result = null;

			// Recursively call all parents until namespace is reached
			var p = type.Parent;
			while (p != null)
			{
				// Add p as parent.
				parentChain.Add(p);

				if (p is IRNamespace)
				{
					result = p as IRNamespace;
					break;
				}

				p = p.Parent;
			}

			// Reverse chain so that it starts with the namespace.
			// The last element in the list is the direct parent of the type.
			parentChain.Reverse();

			return result;
		}

		// Returns a set of namespaces referenced by the given namespace.
		// A namespace is referenced if any type in the given namespace has a property which
		// references a type in another namespace.
		public static List<IRNamespace> ResolveNSReferences(IRNamespace ns)
		{
			HashSet<IRNamespace> references = new HashSet<IRNamespace>();

			// Only classes make references.
			foreach (var irClass in ns.Classes)
			{
				// Loop each property and record the referenced namespace.
				foreach (var prop in irClass.Properties)
				{
					if (prop.Type == PropertyTypeKind.TYPE_REF)
					{
						// Go up in the parent chain to find the containing namespace!
						var parent = prop.ReferencedType.Parent;
						// A non-set parent could wreak havoc here..
						while (!(parent is IRNamespace))
						{
							parent = parent.Parent;
						}
						// Parent should be a namespace instance by now
						references.Add((parent as IRNamespace));
					}
				}
			}

			// Remove reference to our own file.
			references.Remove(ns);
			return references.ToList();
		}

		public static string TypeTostring(PropertyTypeKind type, IRClass current,
										  IRTypeNode reference)
		{
			switch (type)
			{
				case PropertyTypeKind.DOUBLE:
					return "double";
				case PropertyTypeKind.FLOAT:
					return "float";
				case PropertyTypeKind.INT32:
					return "int32";
				case PropertyTypeKind.INT64:
					return "int64";
				case PropertyTypeKind.UINT32:
					return "uint32";
				case PropertyTypeKind.UINT64:
					return "uint64";
				case PropertyTypeKind.FIXED32:
					return "fixed32";
				case PropertyTypeKind.FIXED64:
					return "fixed64";
				case PropertyTypeKind.BOOL:
					return "bool";
				case PropertyTypeKind.STRING:
					return "string";
				case PropertyTypeKind.BYTES:
					return "bytes";
				case PropertyTypeKind.TYPE_REF:
					return ResolveTypeReferenceString(current, reference);
				default:
					throw new Exception("Type not recognized!");
			}
		}

		public static string FieldLabelToString(FieldLabel label, bool proto3)
		{
			switch (label)
			{
				case FieldLabel.OPTIONAL:
					// Proto3 syntax has an implicit OPTIONAL label!
					return (proto3 == true) ? "" : "optional";
				case FieldLabel.REPEATED:
					return "repeated";
				case FieldLabel.REQUIRED:
					// Proto3 syntax does not allow REQUIRED label!
					return (proto3 == true) ? "" : "required";
				default:
					return "";
			}
		}

		public static string DefaultValueToString(string defaultValue, IRTypeNode reference)
		{
			// We return another value if there is a reference made.
			if (reference != null && reference is IREnum)
			{
				// Look for the enum property with the same value.
				var irEnum = reference as IREnum;
				// Care different representations!
				IEnumerable<IREnumProperty> propertyMatch = irEnum.Properties.Where(prop => prop.Value.ToString().Equals(
																defaultValue));
				if (propertyMatch.Any())
				{
					return propertyMatch.First().Name;
				}
			}
			// Default case; return the parameter back.
			return defaultValue;
		}

		// Utility function to prevent misalignment of keywords across surrounding lines when 
		// no string value is used.
		public static string SuffixAlign(this string keyword)
		{
			if (keyword.Length == 0) return "";
			return keyword + " ";
		}
	}
}
