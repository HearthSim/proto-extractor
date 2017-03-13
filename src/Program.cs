using protoextractor.analyzer.c_sharp;
using protoextractor.compiler;
using protoextractor.compiler.proto_scheme;
using protoextractor.processing;
using protoextractor.util;
using System;
using System.IO;

namespace protoextractor
{
	class Program
	{
		public static Logger Log;

		static int Main(string[] args)
		{
			// Setup new logger
			Log = new Logger();

			Log.Info("Launched proto-extractor");

			// Run the test cases.
			// This function will exit the program after testing..
			// ProgramTest.Test();

			// Parse commands
			var opts = new Options();

			if (args == null || args.Length == 0)
			{
				Console.WriteLine(opts.GetUsage(null));

				Log.Exception("Parameters were incorrect");
				Environment.Exit(-2);
			}

			if (!CommandLine.Parser.Default.ParseArgumentsStrict(args, opts, () =>
		{
			Console.WriteLine("Failed to parse arguments!");
				Console.WriteLine();
				Console.WriteLine(opts.GetUsage(null));

				Log.Exception("Parameters were incorrect");
				Environment.Exit(-2);
			}))
			{
				// Error
			}

			// Update logger with command line parameters.
			Log.SetParams(opts);

			// Setup decompiler
			var analyzer = new CSAnalyzer();
			//Set the library path.
			if (!Directory.Exists(opts.LibraryPath))
			{
				Console.WriteLine("The library path does not exist! Exiting..");

				Log.Exception("Library path was not found");
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

			//*----- Searches and resolves circular dependancies -----*//
			DependancyAnalyzer depAnalyzer = new DependancyAnalyzer(program);
			program = depAnalyzer.Process();

			//*----- Uses longest substring matching to group namespaces into common packages -----*//
			AutoPackager nsPackager = new AutoPackager(program);
			program = nsPackager.Process();

			//*----- Manually move matching namespaces into another -----*//
			ManualPackager manualPackager = new ManualPackager(program);
			// Match keywoard is case sensitive!
			manualPackager.AddMapping("pegasus.spectator", "SpectatorProto");
			program = manualPackager.Process();

			//*----- Searches and resolves name collisions of various types -----*//
			NameCollisionAnalyzer ncAnalyzer = new NameCollisionAnalyzer(program);
			program = ncAnalyzer.Process();

			// Setup compiler
			DefaultProtoCompiler compiler = new Proto2Compiler(program);
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
				Log.Info("Created output directory: {0}", fullDirPath);
				// Update options.
				opts.OutDirectory = fullDirPath;

			}
			compiler.SetOutputPath(opts.OutDirectory);

			// Insert special option for the go compiler.
			// compiler.SetFileOption("go_package", Set_GoPackage_Option);

			// Write output
			// All paths for created files are lowercased!
			compiler.Compile();

			Log.Info("Finished extracting");

			return 0;
		}

		//public static string Set_GoPackage_Option(IR.IRNamespace ns, string fileName)
		//{
		//    // Take the short name part of the namespace.
		//    return ns.ShortName.ToLower();
		//}
	}
}
