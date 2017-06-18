using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace protoextractor.compiler.proto_scheme
{
	/*  Protobuffer example file

	        Syntax MUST be first line of the file!
	        We do declare package names.

	        syntax = "proto3";
	        package ns.subns;

	        import "myproject/other_protos.proto";

	        enum EnumAllowingAlias {
	          option allow_alias = true;
	          UNKNOWN = 0;
	          STARTED = 1;
	          RUNNING = 1;
	        }


	        message SearchRequest {
	          required string query = 1;
	          optional int32 page_number = 2;
	          optional int32 result_per_page = 3;
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

	        Each namespace maps to ONE package!

	*/

	class Proto3Compiler : DefaultProtoCompiler
	{
		private static string _StartSpacer = "//----- Begin {0} -----";
		private static string _EndSpacer = "//----- End {0} -----";
		private static string _Spacer = "//------------------------------";

		// String used to reference the original name from the source library.
		private static string _Reference = "{0}ref: {1}";

		private int _incrementCounter;

		// Mapping of all namespace objects to their location on disk.
		private Dictionary<IRNamespace, string> _NSLocationCache;

		public Proto3Compiler(IRProgram program) : base(program)
		{
			_incrementCounter = 0;
		}

		public override void Compile()
		{
			Program.Log.OpenBlock("Proto3Compiler::Compile");
			Program.Log.Info("Writing proto files to folder `{0}`", _path);

			if (DumpMode == true)
			{
				// Dump the program into one file and return.
				Dump();

				Program.Log.CloseBlock();
				return;
			}

			// Process file names.
			// This already includes the proto extension!
			_NSLocationCache = ProtoHelper.NamespacesToFileNames(_program.Namespaces,
																 PackageStructured);

			// Create/Open files for writing.
			foreach (var irNS in _NSLocationCache.Keys)
			{
				// Get filename of current namespace.
				var nsFileName = _NSLocationCache[irNS];
				// Make sure directory structure exists, before creating/writing file.
				var folderStruct = Path.Combine(_path, Path.GetDirectoryName(nsFileName));
				Directory.CreateDirectory(folderStruct);

				// Resolve all imports.
				var references = ProtoHelper.ResolveNSReferences(irNS);

				// Construct file for writing.
				var constructedFileName = Path.Combine(_path, nsFileName);
				var fileStream = File.Create(constructedFileName);
				using (fileStream)
				{
					var textStream = new StreamWriter(fileStream);
					using (textStream)
					{
						// Print file header.
						WriteHeaderToFile(irNS, constructedFileName, textStream);
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
			Program.Log.CloseBlock();
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
					WriteHeaderToFile(null, dumpFileName, textStream);

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

		private void WriteHeaderToFile(IRNamespace ns, string fileName, TextWriter w)
		{
			w.WriteLine("syntax = \"proto3\";");
			if (ns != null)
			{
				var nsPackage = ProtoHelper.ResolvePackageName(ns);
				w.WriteLine("package {0};", nsPackage);
				// Write all file scoped options
				WriteFileOptions(ns, fileName, w);
			}
			w.WriteLine();

			var firstComment = "Proto extractor compiled unit - https://github.com/HearthSim/proto-extractor";
			var fileGeneration = string.Format("File generated at {0}", DateTime.UtcNow);
			WriteComments(w, firstComment, fileGeneration);

			w.WriteLine();
		}

		private void WriteImports(List<IRNamespace> referencedNamespaces, TextWriter w)
		{
			// Get filenames for the referenced namespaces, from cache.
			var nsFileNames = _NSLocationCache.Where(kv => referencedNamespaces.Contains(
														 kv.Key)).Select(kv => kv.Value);
			// Order filenames in ascending order.
			var orderedImports = nsFileNames.OrderBy(x => x);

			foreach (var import in orderedImports)
			{
				// import "myproject/other_protos.proto";
				// IMPORTANT: Forward slashes!
				var formattedImport = import.Replace(Path.DirectorySeparatorChar, '/');
				w.WriteLine("import \"{0}\";", formattedImport);
			}
			// End with additionall newline
			w.WriteLine();
		}

		private void WriteEnumsToFile(IRNamespace ns, TextWriter w, string prefix = "")
		{
			foreach (var irEnum in ns.Enums.OrderBy(e => e.ShortName))
			{
				// Don't write private types.
				if (irEnum.IsPrivate)
				{
					continue;
				}
				WriteEnum(irEnum, w, prefix);
			}
		}

		private void WriteEnum(IREnum e, TextWriter w, string prefix)
		{
			var reference = string.Format(_Reference, prefix, e.OriginalName);
			WriteComments(w, reference);

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
					Name = "AUTO_INVALID_" + _incrementCounter++,
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
			foreach (var prop in propList.OrderBy(prop => prop.Value))
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
			foreach (var irClass in ns.Classes.OrderBy(c => c.ShortName))
			{
				// Don't write private types.
				if (irClass.IsPrivate)
				{
					continue;
				}
				WriteMessage(irClass, w, prefix);
			}
		}

		private void WriteMessage(IRClass c, TextWriter w, string prefix)
		{
			var reference = string.Format(_Reference, prefix, c.OriginalName);
			WriteComments(w, reference);

			// Type names are kept in PascalCase!
			w.WriteLine("{0}message {1} {{", prefix, c.ShortName);
			// Write all private types first!
			WritePrivateTypesToFile(c, w, prefix + "\t");

			// Write all fields last!
			foreach (var prop in c.Properties.OrderBy(prop => prop.Options.PropertyOrder))
			{
				var opts = prop.Options;
				// Proto3 syntax has implicit default values!

				var label = ProtoHelper.FieldLabelToString(opts.Label, true);
				var type = ProtoHelper.TypeTostring(prop.Type, c, prop.ReferencedType);
				var tag = opts.PropertyOrder.ToString();

				// In proto3, the default for a repeated field is PACKED=TRUE.
				// Only if it's not packed.. we set it to false.
				var packed = "";
				if (opts.IsPacked == false && opts.Label == FieldLabel.REPEATED)
				{
					// Incorporate SPACE at the beginning of the string!
					packed = string.Format(" [packed=false]");
				}

				w.WriteLine("{0}{1} {2} {3} = {4}{5};", prefix + "\t", label, type,
							prop.Name.PascalToSnake(), tag, packed);
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
			foreach (var privEnum in enumEnumeration.OrderBy(e => e.ShortName))
			{
				WriteEnum(privEnum, w, prefix);
			}

			// Then all private classes.
			foreach (var privClass in classEnumeration.OrderBy(c => c.ShortName))
			{
				// This recursively writes the private types of this class (if any).
				WriteMessage(privClass, w, prefix);
			}
		}
	}
}
