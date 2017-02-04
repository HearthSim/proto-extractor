using protoextractor.analyzer.c_sharp;
using protoextractor.compiler.proto_scheme;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace protoextractor
{
    class Program
    {
        // Location of Game library files.
        private static string absLibPath = @"D:\Program Files (x86)\Hearthstone-Stove\Hearthstone_Data\Managed";
        // Match function for files to analyze.
        private static string dllFileNameGlob = "Assembly-CSharp*.dll";
        // Output folder for proto files.
        private static string absProtoOutput = Path.GetFullPath(@".\proto-out");
        // Match function for proto files to compile.
        private static string protoFileNameGlob = "*.proto";
        // Output folder for compiled protobuffer files. -> GO
        private static string GO_absCompiledOutput = Path.GetFullPath("compiled_proto_go");
        // Output folder for compiled protobuffer files. -> PYTHON
        private static string PY_absCompiledOutput = Path.GetFullPath("compiled_proto_py");

        static void Main(string[] args)
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
            processing.DependancyAnalyzer depAnalyzer = new processing.DependancyAnalyzer(program);
            program = depAnalyzer.Process();

            // Construct protobuffer files from the parsed data.
            var compiler = new ProtoSchemeCompiler(program);
            // Dumps everything to one file..
            // compiler.DumpMode = true;

            compiler
                // Set the path for writing compiled files.
                .SetOutputPath(absProtoOutput)
                // Write output.
                .Compile();

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
            if(!files.Any())
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
