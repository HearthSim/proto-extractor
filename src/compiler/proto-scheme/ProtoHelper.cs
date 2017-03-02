using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace protoextractor.compiler.proto_scheme
{
	public static class ProtoHelper
	{
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
					var pathPieces = nsName.Split('.').ToList();
					// Use the shortname as filename.
					pathPieces.Add(ns.ShortName + ".proto");
					// Combine all pieces into a valid path structure.
					var path = Path.Combine(pathPieces.ToArray());
					returnValue.Add(ns, path);
				}
			}
			else
			{
				foreach (var ns in nsList)
				{
					returnValue.Add(ns, ns.FullName + ".proto");
				}
			}

			return returnValue;
		}

		// This function converts string in PascalCase to snake_case.
		// eg; BatlleNet => battle_net
		public static string PascalToSnake(this string s)
		{
			var chars = s.Select((c, i) => (char.IsUpper(c)) ? ("_" + c.ToString()) : c.ToString());
			return string.Concat(chars).Trim('_').ToLower();
		}

		public static string ResolvePackageName(IRNamespace ns)
		{
			return ns.FullName.ToLower();
		}

		public static string ResolveTypeReferenceString(IRClass current, IRTypeNode reference)
		{
			var returnValue = "";
			// If current and reference share the same namespace, no package name is added.
			var curNS = GetNamespaceForType(current);
			var refNS = GetNamespaceForType(reference);

			// If the namespaces of both types don't match, the reference is made to another package.
			if (curNS != refNS)
			{
				var pkgRefNS = ResolvePackageName(refNS);
				returnValue = returnValue + pkgRefNS + ".";
			}

			// If reference is a private type, the public parent is added.. unless current
			// IS THE PUBLIC PARENT.
			if (!IsParentOffType(current, reference))
			{
				if (reference.IsPrivate)
				{
					// Find public parent of reference.
					var pubType = FindPublicParent(reference);
					returnValue = returnValue + pubType.ShortName + ".";
				}
			}

			return returnValue + reference.ShortName;
		}

		// Goes up the parent chain looking for the first type that's not private.
		public static IRProgramNode FindPublicParent(IRTypeNode type)
		{
			IRProgramNode checkType = type;
			while (checkType.IsPrivate)
			{
				checkType = checkType.Parent;
			}

			return checkType;
		}

		// Recursively check all parents of child. If one of the parents matches 'parent',
		// TRUE will be returned.
		public static bool IsParentOffType(IRProgramNode parent, IRProgramNode child)
		{
			var p = child.Parent;
			while (p != null)
			{
				if (p == parent)
				{
					return true;
				}

				p = p.Parent;
			}

			return false;
		}

		// Returns the namespace object for the given object.
		public static IRNamespace GetNamespaceForType(IRTypeNode type)
		{
			// Recursively call all parents until namespace is reached
			var p = type.Parent;
			while (p != null)
			{
				if (p is IRNamespace)
				{
					return p as IRNamespace;
				}

				p = p.Parent;
			}

			return null;
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
				IREnum irEnum = reference as IREnum;
				// Care different representations!
				var propertyMatch = irEnum.Properties.Where(prop => prop.Value.ToString().Equals(
																defaultValue));
				if (propertyMatch.Any())
				{
					return propertyMatch.First().Name;
				}
			}
			// Default case; return the parameter back.
			return defaultValue;
		}
	}
}
