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

			// Parse commands
			var opts = new ExtendedOptions();

			if (args == null || args.Length == 0)
			{
				Console.WriteLine(opts.GetUsage(null));

				Log.Exception("Parameters were incorrect");
				Environment.Exit(-2);
			}

			CommandLine.Parser.Default.ParseArgumentsStrict(args, opts, () =>
			{
				Console.WriteLine("Failed to parse arguments!");
				Console.WriteLine();
				Console.WriteLine(opts.GetUsage(null));

				Log.Exception("Parameters were incorrect");
				Environment.Exit(-2);
			});

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

			//************************************************************
			try
			{
				//*----- Lowercase short- and fullnames of all namespacs -----*//
				LowerCaseNamespaces lcProcessor = new LowerCaseNamespaces(program);
				program = lcProcessor.Process();

				if (opts.ManualPackagingFile.Length > 0)
				{
					//*----- Manually move matching namespaces into another -----*//
					ManualPackager manualPackager = new ManualPackager(program, opts.ManualPackagingFile);
					program = manualPackager.Process();
				}

				if (opts.ResolveCircDependancies)
				{
					//*----- Searches and resolves circular dependancies -----*//
					DependancyAnalyzer depAnalyzer = new DependancyAnalyzer(program);
					program = depAnalyzer.Process();
				}

				if (opts.AutomaticPackaging)
				{
					//*----- Uses longest substring matching to group namespaces into common packages -----*//
					AutoPackager nsPackager = new AutoPackager(program);
					program = nsPackager.Process();
				}

				if (opts.ResolveCollisions)
				{
					//*----- Searches and resolves name collisions of various types -----*//
					NameCollisionAnalyzer ncAnalyzer = new NameCollisionAnalyzer(program);
					program = ncAnalyzer.Process();
				}

				//************************************************************
			}
			catch (Exception e)
			{
				Log.Exception("Exception occurred while processing!", e);
				Environment.Exit(-8);
			}

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

			// Write output
			// All paths for created files are lowercased!
			compiler.Compile();

			Log.Info("Finished extracting");

			return 0;
		}
	}
}
