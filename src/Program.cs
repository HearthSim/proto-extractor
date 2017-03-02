using protoextractor.analyzer.c_sharp;
using protoextractor.compiler;
using protoextractor.compiler.proto_scheme;
using protoextractor.processing;
using System;
using System.IO;

namespace protoextractor
{
	class Program
	{
		static int Main(string[] args)
		{
			// Run the test cases.
			// This function will exit the program after testing..
			// ProgramTest.Test();

			// Parse commands
			var opts = new Options();

			if (args == null || args.Length == 0)
			{
				Console.WriteLine(opts.GetUsage(null));
				Environment.Exit(-2);
			}

			if (!CommandLine.Parser.Default.ParseArgumentsStrict(args, opts, () =>
		{
			Console.WriteLine("Failed to parse arguments!");
				Console.WriteLine();
				Console.WriteLine(opts.GetUsage(null));
				Environment.Exit(-2);
			}))
			{
				// Error
			}

			// Setup decompiler
			var analyzer = new CSAnalyzer();
			//Set the library path.
			if (!Directory.Exists(opts.LibraryPath))
			{
				Console.WriteLine("The library path does not exist! Exiting..");
				Environment.Exit(-1);
			}
			else
			{
				analyzer.SetLibraryPath(opts.LibraryPath);
			}
			// Set input files.
			analyzer.InputFiles = opts.InputFileName;

			// Analyze
			analyzer.Parse();

			// Fetch the root for program inspection
			var program = analyzer.GetRoot();

			DependancyAnalyzer dAnalyzer = new DependancyAnalyzer(program);
			program = dAnalyzer.Process();
			NamespacePackager nPackager = new NamespacePackager(program);
			program = nPackager.Process();
			NameCollisionAnalyzer ncAnalyzer = new NameCollisionAnalyzer(program);
			program = ncAnalyzer.Process();

			// Setup compiler
			DefaultCompiler compiler = new Proto2Compiler(program);
			if (opts.Proto3Syntax == true)
			{
				compiler = new Proto3Compiler(program);
			}

			if (!Directory.Exists(opts.OutDirectory))
			{
				// Generate full path for directory.
				var fullDirPath = Path.GetFullPath(opts.OutDirectory);
				// Create directory.
				Directory.CreateDirectory(fullDirPath);
				Console.WriteLine("Created output directory: {0}", fullDirPath);
				// Update options.
				opts.OutDirectory = fullDirPath;

			}
			compiler.SetOutputPath(opts.OutDirectory);

			// Insert special option for the go compiler.
			// compiler.SetFileOption("go_package", Set_GoPackage_Option);

			// Write output
			compiler.Compile();

			return 0;
		}

		public static string Set_GoPackage_Option(IR.IRNamespace ns, string fileName)
		{
			// Take the short name part of the namespace.
			return ns.ShortName.ToLower();
		}
	}
}
