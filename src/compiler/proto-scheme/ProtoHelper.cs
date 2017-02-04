using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.compiler.proto_scheme
{
    public static class ProtoHelper
    {
        // Converts given namespace objects into paths.
        // The returned string is a relative path to the set '_path' property!
        public static Dictionary<IRNamespace, string> NamespacesToFileNames(List<IRNamespace> nsList, bool structured)
        {
            Dictionary<IRNamespace, string> returnValue = new Dictionary<IRNamespace, string>();

            if (structured == true)
            {
                // Do something fancy to figure out folder names.
                foreach (var mNS in nsList)
                {
                    var highestMatchCount = 0;
                    foreach (var cmpNS in nsList)
                    {
                        if (mNS == cmpNS) continue;

                        // Compare all namespaces against each other and look for the
                        // substring of mNS that resembles the most to ONE other namespace.
                        // We prefer to not have half-words as subnamespace, so we cut to the last DOT char.
                        var str = LongestmatchingSubstring(mNS.FullName, cmpNS.FullName);
                        var matchLength = str.Count();
                        // Cut till last dot, if there is one.
                        var lastDotIdx = str.LastIndexOf('.');
                        matchLength = (lastDotIdx != -1) ? lastDotIdx : matchLength;

                        if (matchLength > highestMatchCount)
                        {
                            highestMatchCount = matchLength;
                        }
                    }

                    string slice;
                    if (highestMatchCount == 0)
                    {
                        // Take full name.
                        slice = mNS.FullName;
                    }
                    else
                    {
                        // Take the matching substring. Also trim dots from the substring, to 
                        // prevent path generation problems.
                        slice = mNS.FullName.Substring(0, highestMatchCount).Trim('.');
                    }
                    // Construct path for namespace.
                    var pathPieces = slice.Split('.').ToList();
                    // Use the shortname as last foldername above the file.
                    pathPieces.Add(mNS.ShortName);
                    // Use the shortname as filename.
                    pathPieces.Add(mNS.ShortName + ".proto");
                    // DO not repeat string values, while preserving the given order.
                    var distinctPieces = pathPieces.Distinct().ToArray();
                    var path = Path.Combine(distinctPieces);
                    returnValue.Add(mNS, path);
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

        // Returns the longest substring that matches 2 or more types of the given list.
        // The substrings are taken from the namespaces of each given type!
        // The substring always starts from index 0 for each fullname.
        // It's an heuristic for the 'longest matching substring problem'.
        public static string LongestmatchingSubstring(string subject, string matcher)
        {
            // Loop all string characters..
            int i;
            var strOne = subject;
            var strCmp = matcher;
            for (i = 0; i < strOne.Count() && i < strCmp.Count(); i++)
            {
                var c1 = strOne[i];
                var c2 = strCmp[i];
                // If mismatch, return.
                if (!c1.Equals(c2)) break;
            }

            return strOne.Substring(0, i);
        }

        // This function converts string in PascalCase to snake_case
        // eg; BatlleNet => battle_net
        public static string PascalToSnake(this string s)
        {
            var chars = s.Select((c, i) => (char.IsUpper(c)) ? ("_" + c.ToString()) : c.ToString());
            return string.Concat(chars).Trim('_').ToLower();
        }

        public static string ResolvePackageName(IRNamespace ns, Dictionary<IRNamespace, string> nsFileLocation)
        {
            var nsFilePath = nsFileLocation[ns];
            var nsPackageStructure = Path.GetDirectoryName(nsFilePath).Replace(Path.DirectorySeparatorChar, '.');
            return nsPackageStructure;
        }

        public static string ResolveTypeReferenceString(IRClass current, IRTypeNode reference,
            Dictionary<IRNamespace, string> nsFileLocation)
        {
            var returnValue = "";
            // If current and reference share the same namespace, no package name is added.
            var curNS = GetNamespaceForType(current);
            var refNS = GetNamespaceForType(reference);

            // If the namespaces of both types don't match, the reference is made to another package.
            if (curNS != refNS)
            {
                var pkgRefNS = ResolvePackageName(refNS, nsFileLocation);
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

        public static string TypeTostring(PropertyTypeKind type, IRClass current, IRTypeNode reference,
            Dictionary<IRNamespace, string> nsFileLocations)
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
                    return ResolveTypeReferenceString(current, reference, nsFileLocations);
                default:
                    throw new Exception("Type not recognized!");
            }
        }

        public static string FieldLabelToString(FieldLabel label)
        {
            switch (label)
            {
                case FieldLabel.OPTIONAL:
                    // Proto3 syntax has an implicit OPTIONAL label.
                    return "";
                case FieldLabel.REPEATED:
                    return "repeated";
                case FieldLabel.REQUIRED:
                    return "required";
                default:
                    return "";
            }
        }
    }
}
