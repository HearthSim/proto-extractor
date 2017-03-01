using protoextractor.analyzer.c_sharp;
using protoextractor.compiler;
using protoextractor.compiler.proto_scheme;
using protoextractor.processing;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace protoextractor
{
    /*
     * Launch parameters for HearthStone decompilation:
     *      absLibPath = @"D:\Program Files (x86)\Hearthstone-Stove\Hearthstone_Data\Managed"
     *      dllFileNameGlob = "Assembly-CSharp*.dll"
     *      
     * Launch parameters for other decompilation:
     *      absLibPath = @"E:\User Data\Documenten\Visual Studio 2015\Projects\CSProtoBuffCompilation\bin\Debug"
     *      dllFileNameGlob = "CSProtoBuffCompilation.exe"
     */
    class Program
    {
        // Location of Game library files.
        private static string absLibPath = @"D:\Program Files (x86)\Hearthstone\Hearthstone_Data\Managed";
        // Match function for files to analyze.
        private static string dllFileNameGlob = "Assembly-CSharp*.original.dll";
        // Output folder for proto files.
        private static string absProtoOutput = Path.GetFullPath(@".\proto-out");
        // Match function for proto files to compile.
        private static string protoFileNameGlob = "*.proto";
        // Output folder for compiled protobuffer files. -> GO
        private static string GO_absCompiledOutput = Path.GetFullPath("compiled_proto_go");
        // Output folder for compiled protobuffer files. -> PYTHON
        private static string PY_absCompiledOutput = Path.GetFullPath("compiled_proto_py");
        // Output folder for compiled protobuffer files. -> C#
        private static string CS_absCompiledOutput = Path.GetFullPath("compiled_proto_cs");

        static int Main(string[] args)
        {
            //Test();

            // Parse commands
            var opts = new Options();

            if (args == null || args.Length == 0)
            {
                Console.WriteLine(opts.GetUsage(null));
                Environment.Exit(-2);
            }

            if (!CommandLine.Parser.Default.ParseArgumentsStrict(args, opts,
                () =>
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
            compiler.SetFileOption("go_package", Set_GoPackage_Option);

            // Write output
            compiler.Compile();

            return 0;
        }

        public static string Set_GoPackage_Option(IR.IRNamespace ns, string fileName)
        {
            // Take the short name part of the namespace.
            return ns.ShortName.ToLower();
        }

        // -------------------------------------------------------------

        public static void Test()
        {
            // Setup analyzer
            var analyzer = new CSAnalyzer();
            analyzer
                // Set the path where all libraries are located.
                .SetLibraryPath(absLibPath)
                // Select all libraries matching this pattern.
                .SetFileGlob(dllFileNameGlob)
                // Parse all matching libraries
                .Parse();

            // Fetch the IL program root from the analyzer.
            var program = analyzer.GetRoot();

            // Analyze and solve circular dependancies.
            DependancyAnalyzer depAnalyzer = new DependancyAnalyzer(program);
            program = depAnalyzer.Process();

            // Group matching namespaces under a common package.
            NamespacePackager nsPackager = new NamespacePackager(program);
            program = nsPackager.Process();

            // Analyze and fix name collisions.
            NameCollisionAnalyzer nameAnalyzer = new NameCollisionAnalyzer(program);
            program = nameAnalyzer.Process();

            // Construct protobuffer files from the parsed data.
            DefaultCompiler compiler;
            // Use proto3 syntax.
            // compiler = new Proto3Compiler(program);
            // Use proto2 syntax.
            compiler = new Proto2Compiler(program);

            // Dumps everything to one file..
            // compiler.DumpMode = true;

            compiler
                // Set the path for writing compiled files.
                .SetOutputPath(absProtoOutput)
                // Write output.
                .Compile();

            //CSharp_TestDecompiledProtoFiles();

            Python_TestDecompiledProtoFiles();

            Go_TestDecompiledProtoFiles();

            return;
        }

        public static void Python_TestDecompiledProtoFiles()
        {
            // All proto files are written to their respective .proto files.
            // Collect them and launch the proto compiler!
            string[] files = Directory.GetFiles(absProtoOutput, protoFileNameGlob, SearchOption.AllDirectories);
            // Generate absolute paths enclosed with quotes.
            files = files.Select(x => "\"" + Path.GetFullPath(x) + "\"").ToArray();
            // Create folder for compiler proto files.
            Directory.CreateDirectory(PY_absCompiledOutput);

            // Construct arguments string for protocompiling to PYTHON output.
            string proto_args = "--proto_path=\"" + absProtoOutput + "\" --python_out=\"" + PY_absCompiledOutput + "\" "
                + string.Join(" ", files);

            // Setup protoc process..
            Process protoc = new Process();
            protoc.StartInfo = new ProcessStartInfo()
            {
                FileName = "protoc",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                Arguments = proto_args,
            };
            protoc.Start();
            Console.WriteLine(proto_args);

            while (!protoc.HasExited)
            {
                Thread.Sleep(200);
                Console.Write(protoc.StandardOutput.ReadToEnd());
            }

            // Print all subprocess output to console.
            Console.Write(protoc.StandardOutput.ReadToEnd());
        }

        // Actually almost the same code as Python_TestDecompiledProtoFiles(..)
        public static void CSharp_TestDecompiledProtoFiles()
        {
            // All proto files are written to their respective .proto files.
            // Collect them and launch the proto compiler!
            string[] files = Directory.GetFiles(absProtoOutput, protoFileNameGlob, SearchOption.AllDirectories);
            // Generate absolute paths enclosed with quotes.
            files = files.Select(x => "\"" + Path.GetFullPath(x) + "\"").ToArray();
            // Create folder for compiler proto files.
            Directory.CreateDirectory(CS_absCompiledOutput);

            // Construct arguments string for protocompiling to PYTHON output.
            string proto_args = "--proto_path=\"" + absProtoOutput + "\" --csharp_out=\"" + CS_absCompiledOutput + "\" "
                + string.Join(" ", files);

            // Setup protoc process..
            Process protoc = new Process();
            protoc.StartInfo = new ProcessStartInfo()
            {
                FileName = "protoc",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                Arguments = proto_args,
            };
            protoc.Start();
            Console.WriteLine(proto_args);

            while (!protoc.HasExited)
            {
                Thread.Sleep(200);
                Console.Write(protoc.StandardOutput.ReadToEnd());
            }

            // Print all subprocess output to console.
            Console.Write(protoc.StandardOutput.ReadToEnd());
        }

        public static void Go_TestDecompiledProtoFiles()
        {
            // The developer of protoc-gen-go is not a flexible guy and wants one call of protoc-gen-go per package..
            // This means calling protoc per PACKAGE -> per subdirectory under ProtoOutputPath.
            // Protoc-gen-go is a plugin for the proto compiler, install it by running:
            // go get -u github.com/golang/protobuf/protoc-gen-go

            // Check if there are files directly under absProtoOutput.
            if (Directory.GetFiles(absProtoOutput).Any())
            {
                GO_RunProtocOnDirectory(absProtoOutput);
            }

            // Iterate over all direct subdirectories of absProtoOutput.
            var packages = Directory.GetDirectories(absProtoOutput).ToList();

            while (packages.Any())
            {
                var packageDir = packages.ElementAt(0);
                packages.RemoveAt(0);

                GO_RunProtocOnDirectory(packageDir);

                // Recursive run on subdirectory/subpackage.
                packages.AddRange(Directory.GetDirectories(packageDir));
            }
        }

        private static void GO_RunProtocOnDirectory(string directory)
        {
            // All proto files are written to their respective .proto files.
            // Collect them from the top directory.
            string[] files = Directory.GetFiles(directory, protoFileNameGlob, SearchOption.TopDirectoryOnly);

            // Don't run if there are no proto files found!.
            if (!files.Any())
            {
                return;
            }

            // Generate absolute paths enclosed with quotes.
            files = files.Select(x => "\"" + Path.GetFullPath(x) + "\"").ToArray();
            // Create folder for compiler proto files.
            Directory.CreateDirectory(GO_absCompiledOutput);

            // Arguments for GO output.
            // The files to process are all located within the same package!
            string proto_args = "--proto_path=\"" + absProtoOutput + "\" --go_out=\"" + GO_absCompiledOutput + "\" "
                + string.Join(" ", files);

            // Setup protoc process..
            Process protoc = new Process();
            protoc.StartInfo = new ProcessStartInfo()
            {
                FileName = "protoc",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                Arguments = proto_args,
            };
            protoc.Start();
            Console.WriteLine(proto_args);

            while (!protoc.HasExited)
            {
                Thread.Sleep(200);
                Console.Write(protoc.StandardOutput.ReadToEnd());
            }

            // Print all subprocess output to console.
            Console.Write(protoc.StandardOutput.ReadToEnd());

            Console.WriteLine("Proto compiler finished succesfully!");
        }
    }
}
