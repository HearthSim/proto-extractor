using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using protoextractor.IR;
using System.IO;

namespace protoextractor.compiler.proto_scheme
{
    /* Protobuffer example file
         * 
         *  Syntax MUST be first line of the file!
         *  We do not declare package names.
         *  
         *  syntax = "proto2";
         *  import "myproject/other_protos.proto";
         *  
         *  enum EnumAllowingAlias {
              option allow_alias = true;
              UNKNOWN = 0;
              STARTED = 1;
              RUNNING = 1;
            }
         *  
         *  
         *  message SearchRequest {
              required string query = 1;
              optional int32 page_number = 2;
              optional int32 result_per_page = 3 [default = 10];
              enum Corpus {
                UNIVERSAL = 0;
                WEB = 1;
                IMAGES = 2;
                LOCAL = 3;
                NEWS = 4;
                PRODUCTS = 5;
                VIDEO = 6;
              }
              optional Corpus corpus = 4 [default = UNIVERSAL];
            }

        */

    class ProtoSchemeCompiler : DefaultCompiler
    {
        private static string _StartSpacer = "//----- Begin {0} -----";
        private static string _EndSpacer = "//----- End {0} -----";
        private static string _Spacer = "//------------------------------";

        // The name of the file which will contain the whole dumped IR program.
        private static string _dumpFileName = "dump.proto";

        // If TRUE, this object will dump the whole program to a single file.
        public bool DumpMode { get; set; }

        private int _incrementCounter;

        public ProtoSchemeCompiler(IRProgram program) : base(program)
        {
            DumpMode = false;
            _incrementCounter = 0;
        }

        // Converts given namespace objects into paths.
        // The returned string is a relative path to the set '_path' property!
        public List<string> NamespacesToFileNames(List<IRNamespace> nsList)
        {
            return nsList.Select((ns, res) => ns.FullName + ".proto").ToList();
        }

        public override void Compile()
        {
            if (DumpMode == true)
            {
                // Dump and return.
                Dump();
                return;
            }

            // Process file names.
            // This already includes the proto extension!
            List<string> nsFileNames = NamespacesToFileNames(_program.Namespaces);

            // Create/Open files for writing.
            foreach (var nsFileName in nsFileNames)
            {
                // Get target namespace.
                var nsName = Path.GetFileNameWithoutExtension(nsFileName);
                var irNS = _program.Namespaces.First(ns => ns.FullName.Equals(nsName));

                // Resolve all imports.
                var refSet = ResolveNSReferences(irNS);
                List<IRNamespace> references = new List<IRNamespace>(refSet);

                // Construct file for writing.
                var constructedFileName = Path.Combine(_path, nsFileName);
                var fileStream = File.Create(constructedFileName);
                using (fileStream)
                {
                    var textStream = new StreamWriter(fileStream);
                    using (textStream)
                    {
                        // Print file header.
                        WriteHeaderToFile(irNS, textStream);
                        // Print imports.
                        WriteImports(references, textStream);
                        // Write all enums..
                        WriteEnumsToFile(irNS, textStream, "");
                        // Followed by all messages.
                        WriteClassesToFile(irNS, textStream, "");
                    }
                }
            }

            // Finish up..
        }

        private void Dump()
        {
            // Open the dumpfile for writing.
            var dumpFileName = Path.Combine(_path, _dumpFileName);
            var dumpFileStream = File.Create(dumpFileName);
            using (dumpFileStream)
            {
                // Construct a textwriter for easier printing.
                var textStream = new StreamWriter(dumpFileStream);
                using (textStream)
                {
                    // Print file header.
                    WriteHeaderToFile(null, textStream);

                    // Loop each namespace and write to the dump file.
                    foreach (var ns in _program.Namespaces)
                    {
                        // Start with namespace name..
                        textStream.WriteLine(_StartSpacer, ns.ShortName);
                        textStream.WriteLine();

                        // No imports

                        // Write all public enums..
                        WriteEnumsToFile(ns, textStream);
                        // Write all classes..
                        WriteClassesToFile(ns, textStream);

                        // End with spacer
                        textStream.WriteLine();
                        textStream.WriteLine(_EndSpacer, ns.ShortName);
                        textStream.WriteLine(_Spacer);
                    }
                }
            }
        }

        private void WriteHeaderToFile(IRNamespace ns, TextWriter w)
        {
            w.WriteLine("syntax = \"proto3\";");
            if (ns != null)
            {
                w.WriteLine("package {0};", ns.ShortName);
            }
            w.WriteLine();
            w.WriteLine("// Protobuffer decompiler");
            w.WriteLine("// File generated at {0}", DateTime.UtcNow);
            w.WriteLine();
        }

        private void WriteImports(List<IRNamespace> referencedNamespaces, TextWriter w)
        {
            // Get filenames for the referenced namespaces.
            var nsFileNames = NamespacesToFileNames(referencedNamespaces);
            // Order filenames in ascending order.
            var orderedImports = nsFileNames.OrderBy(x => x);

            foreach (var import in orderedImports)
            {
                // import "myproject/other_protos.proto";
                w.WriteLine("import \"{0}\";", import);
            }
            // End with additionall newline
            w.WriteLine();
        }

        private void WriteEnumsToFile(IRNamespace ns, TextWriter w, string prefix = "")
        {
            foreach (var irEnum in ns.Enums)
            {
                // Don't write private types.
                if (irEnum.IsPrivate) continue;
                WriteEnum(irEnum, w, prefix);
            }
        }

        private void WriteEnum(IREnum e, TextWriter w, string prefix)
        {
            // Type names are kept in PascalCase!
            w.WriteLine("{0}enum {1} {{", prefix, e.ShortName);

            // WE SUPPOSE there are NOT multiple properties with the same value.

            // Make a copy of all properties.
            var propList = e.Properties.ToList();
            // Find or create the property with value 0, that one must come first!
            IREnumProperty zeroProp;
            var zeroPropEnumeration = propList.Where(prop => prop.Value == 0);
            if (!zeroPropEnumeration.Any())
            {
                zeroProp = new IREnumProperty()
                {
                    // Enum values are all shared within the same namespace, so they must be
                    // globally unique!
                    Name = "AUTO_ADD_INVALID_" + _incrementCounter++,
                    Value = 0,
                };
            }
            else
            {
                zeroProp = zeroPropEnumeration.First();
                // And remove the property from the collection.
                propList.Remove(zeroProp);
            }

            // Write the zero property first - AS REQUIRED PER PROTO3!
            w.WriteLine("{0}{1} = {2};", prefix + "\t", zeroProp.Name, zeroProp.Value);

            // Write out the other properties of the enum next
            foreach (var prop in propList)
            {
                // Enum property names are NOT converted to snake case!
                w.WriteLine("{0}{1} = {2};", prefix + "\t", prop.Name, prop.Value);
            }

            // End enum.
            w.WriteLine("{0}}}", prefix);
            w.WriteLine();
        }

        private void WriteClassesToFile(IRNamespace ns, TextWriter w, string prefix = "")
        {
            foreach (var irClass in ns.Classes)
            {
                // Don't write private types.
                if (irClass.IsPrivate) continue;
                WriteMessage(irClass, w, prefix);
            }
        }

        private void WriteMessage(IRClass c, TextWriter w, string prefix)
        {
            // Type names are kept in PascalCase!
            w.WriteLine("{0}message {1} {{", prefix, c.ShortName);
            // Write all private types first!
            WritePrivateTypesToFile(c, w, prefix + "\t");

            // Write all fields last!
            foreach (var prop in c.Properties)
            {
                var opts = prop.Options;
                // No default value is incorporated!
                // TODO ^^^
                var label = ProtoHelper.FieldLabelToString(opts.Label);
                var type = ProtoHelper.TypeTostring(prop.Type, c, prop.ReferencedType);
                var tag = opts.PropertyOrder.ToString();

                // A property can only be packed if it's flag is set AND
                // the property is repeated inside the message.
                var packed = "";
                if (opts.IsPacked == true && opts.Label == FieldLabel.REPEATED)
                {
                    // Incorporate SPACE at the beginning of the string!
                    packed = string.Format(" [packed=true]");
                }

                w.WriteLine("{0}{1} {2} {3} = {4}{5};", prefix + "\t", label, type, prop.Name.PascalToSnake(), tag, packed);
            }

            // End message.
            w.WriteLine("{0}}}", prefix);
            w.WriteLine();
        }

        private void WritePrivateTypesToFile(IRClass cl, TextWriter w, string prefix)
        {
            // Select enums and classes seperately.
            var enumEnumeration = cl.PrivateTypes.Where(x => x is IREnum).Cast<IREnum>();
            var classEnumeration = cl.PrivateTypes.Where(x => x is IRClass).Cast<IRClass>();

            // Write out each private enum first..
            foreach (var privEnum in enumEnumeration)
            {
                WriteEnum(privEnum, w, prefix);
            }

            // Then all private classes.
            foreach (var privClass in classEnumeration)
            {
                // This recursively writes the private types of this class (if any).
                WriteMessage(privClass, w, prefix);
            }
        }

        // Returns a set of namespaces referenced by the given namespace.
        // A namespace is referenced if any type in the given namespace has a property which 
        // references a type in another namespace.
        private HashSet<IRNamespace> ResolveNSReferences(IRNamespace ns)
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
            return references;
        }
    }

    public static class ProtoHelper
    {
        // This function converts string in PascalCase to snake_case
        // eg; BatlleNet => battle_net
        public static string PascalToSnake(this string s)
        {
            var chars = s.Select((c, i) => (char.IsUpper(c)) ? ("_" + c.ToString()) : c.ToString());
            return string.Concat(chars).Trim('_').ToLower();
        }

        public static string ResolvePrivateTypeString(IRClass current, IRTypeNode reference)
        {
            var returnValue = "";
            // If current and reference share the same namespace, no package name is added.
            var curNS = GetNamespaceForType(current);
            var refNS = GetNamespaceForType(reference);

            if (curNS != refNS)
            {
                returnValue = returnValue + refNS.ShortName + ".";
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

        public static string TypeTostring(PropertyTypeKind type, IRClass current, IRTypeNode reference)
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
                    return ResolvePrivateTypeString(current, reference);
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
