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
        // Output folder for compiled protobuffer files.
        private static string absCompiledOutput = Path.GetFullPath("compiled_proto");

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

            // Analyze and handle namespace short names.
            // TOGGLE THIS PROCESSOR TO HAVE SMALLER PACKAGE NAMES!
            //processing.ShortNameAnalyzer nsAnalyzer = new processing.ShortNameAnalyzer(program);
            //program = nsAnalyzer.Process();

            // Construct protobuffer files from the parsed data.
            var compiler = new ProtoSchemeCompiler(program);
            // Dumps everything to one file..
            // compiler.DumpMode = true;

            compiler
                // Set the path for writing compiled files.
                .SetOutputPath(absProtoOutput)
                // Write output.
                .Compile();

            TestDecompiledProtoFiles();

            return;
        }

        public static void TestDecompiledProtoFiles()
        {
            // All proto files are written to their respective .proto files.
            // Collect them and launch the proto compiler!
            string[] files = Directory.GetFiles(absProtoOutput, protoFileNameGlob);
            // Generate absolute paths enclosed with quotes.
            files = files.Select(x => "\"" + Path.GetFullPath(x) + "\"").ToArray();
            // Create folder for compiler proto files.
            Directory.CreateDirectory(absCompiledOutput);

            // Construct arguments string for protocompiling to python files.
            string proto_args = "--proto_path=\"" + absProtoOutput + "\" --python_out=\"" + absCompiledOutput + "\" "
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
            Console.WriteLine("Proto-compiler is running!");

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
