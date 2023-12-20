using CommandLine;
using protoextractor.analyzer.c_sharp;
using protoextractor.compiler;
using protoextractor.compiler.proto_scheme;
using protoextractor.processing;
using protoextractor.util;
using System;
using System.Collections.Generic;
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
            Options opts = null;
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(_opts => opts = _opts);
            if (opts == null) return -1;

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
            analyzer.InputFiles = new List<string>(opts.InputFileNames);

            if (opts.IncludeEnums != null)
            {
                analyzer.SetIncludeEnums(opts.IncludeEnums.Split(new char[] { ',' }));
            }


            // Analyze
            analyzer.Parse();

            // Fetch the root for program inspection
            IR.IRProgram program = analyzer.GetRoot();

            //************************************************************
            try
            {
                //*----- Lowercase short- and fullnames of all namespacs -----*//
                var lcProcessor = new LowerCaseNamespaces(program);
                program = lcProcessor.Process();

                if (opts.ManualPackagingFile.Length > 0)
                {
                    //*----- Manually move matching namespaces into another -----*//
                    var manualPackager = new ManualPackager(program, opts.ManualPackagingFile);
                    program = manualPackager.Process();
                }

                if (opts.ResolveCircDependencies)
                {
                    //*----- Searches and resolves circular dependencies -----*//
                    var depAnalyzer = new DependencyAnalyzer(program);
                    program = depAnalyzer.Process();
                }

                if (opts.AutomaticPackaging)
                {
                    //*----- Uses longest substring matching to group namespaces into common packages -----*//
                    var nsPackager = new AutoPackager(program);
                    program = nsPackager.Process();
                }

                if (opts.ResolveCollisions)
                {
                    //*----- Searches and resolves name collisions of various types -----*//
                    var ncAnalyzer = new NameCollisionAnalyzer(program);
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
            DefaultProtoCompiler compiler = null;
            if (opts.Proto3Syntax == true)
            {
                compiler = new Proto3Compiler(program);
            }
            else
            {
                compiler = new Proto2Compiler(program);
            }

            if (!Directory.Exists(opts.OutDirectory))
            {
                // Generate full path for directory.
                string fullDirPath = Path.GetFullPath(opts.OutDirectory);
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
