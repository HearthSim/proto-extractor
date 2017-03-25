using Mono.Cecil;
using protoextractor.analyzer.c_sharp;
using protoextractor.compiler;
using protoextractor.compiler.proto_scheme;
using protoextractor.IR;
using protoextractor.processing;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace protoextractor
{
	/*
	    Launch parameters for HearthStone decompilation:
	        absLibPath = @"D:\Program Files (x86)\Hearthstone-Stove\Hearthstone_Data\Managed"
	        dllFileNameGlob = "Assembly-CSharp*.dll"

	    Launch parameters for other decompilation:
	        absLibPath = @"E:\User Data\Documenten\Visual Studio 2015\Projects\CSProtoBuffCompilation\bin\Debug"
	        dllFileNameGlob = "CSProtoBuffCompilation.exe"
	*/
	class ProgramTest
	{
		// Location of Game library files.
		private static string absLibPath =
			@"D:\Program Files (x86)\Hearthstone\Hearthstone_Data\Managed";
		// Match function for files to analyze.
		private static string dllFileNameGlob = "Assembly-CSharp*.dll";
		// File indicating which types to move manually
		private static string manualMoveConfig =
			@"D:\Go\src\github.com\HearthSim\proto-extractor\stove-proto-packaging.ini";

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

		public static void Test()
		{
			Program.Log.SetParams(new util.Options()
			{
				DebugMode = true
			});

			Program.Log.OpenBlock("ProgramTest::Test");

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

			// Namespaces should always be lowercased!
			LowerCaseNamespaces lcProcessor = new LowerCaseNamespaces(program);
			program = lcProcessor.Process();

			ManualPackager manPackager = new ManualPackager(program, manualMoveConfig);
			program = manPackager.Process();

			try
			{
				// Analyze and solve circular dependancies.
				DependancyAnalyzer depAnalyzer = new DependancyAnalyzer(program);
				// Use DryRun to only report without solving!
				// depAnalyzer.DryRun();
				program = depAnalyzer.Process();
			}
			catch (CircularException<IRTypeNode> e)
			{
				// The exception came from colliding namespaces.
				if (e.InnerException != null && e.InnerException is CircularException<IRNamespace>)
				{
					// Get circle of namespaces.
					var circle = string.Join(" -> ",
											 ((CircularException<IRNamespace>)e.InnerException).CircularDependancies);
					// Also print the offending types.
					var offendingTypes = e.CircularDependancies;
					var strOffendingTypes = string.Join("\n", offendingTypes);

					Program.Log.Exception("Circular dependancy found between namespaces `{0}`\nOffending types:\n{1}",
										  e, circle, strOffendingTypes);
				}
				else // The exception came from colliding types.
				{
					var circle = string.Join(" -> ", e.CircularDependancies);
					Program.Log.Exception("Circular dependancy found between types `{0}`", e, circle);
				}

				Environment.Exit(-1);
			}

			// Group matching namespaces under a common package.
			// This can be done before or after resolving the collisions because no
			// relations are added between programnodes.
			AutoPackager nsPackager = new AutoPackager(program);
			program = nsPackager.Process();

			// Analyze and fix name collisions.
			NameCollisionAnalyzer nameAnalyzer = new NameCollisionAnalyzer(program);
			program = nameAnalyzer.Process();

			// Construct protobuffer files from the parsed data.
			DefaultProtoCompiler compiler;
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

			////CSharp_TestDecompiledProtoFiles();

			Program.Log.OpenBlock("Python compilation");
			Python_TestDecompiledProtoFiles();
			Program.Log.CloseBlock();

			//Go_TestDecompiledProtoFiles();

			// End the program.
			Program.Log.CloseBlock();
			Environment.Exit(0);
		}

		public static void Python_TestDecompiledProtoFiles()
		{
			// All proto files are written to their respective .proto files.
			// Collect them and launch the proto compiler!
			string[] files = Directory.GetFiles(absProtoOutput, protoFileNameGlob,
												SearchOption.AllDirectories);
			// Generate absolute paths enclosed with quotes.
			files = files.Select(x => "\"" + Path.GetFullPath(x) + "\"").ToArray();
			// Create folder for compiler proto files.
			Directory.CreateDirectory(PY_absCompiledOutput);

			// Construct arguments string for protocompiling to PYTHON output.
			string proto_args = "--proto_path=\"" + absProtoOutput + "\" --python_out=\"" +
								PY_absCompiledOutput + "\" "
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
			string[] files = Directory.GetFiles(absProtoOutput, protoFileNameGlob,
												SearchOption.AllDirectories);
			// Generate absolute paths enclosed with quotes.
			files = files.Select(x => "\"" + Path.GetFullPath(x) + "\"").ToArray();
			// Create folder for compiler proto files.
			Directory.CreateDirectory(CS_absCompiledOutput);

			// Construct arguments string for protocompiling to PYTHON output.
			string proto_args = "--proto_path=\"" + absProtoOutput + "\" --csharp_out=\"" +
								CS_absCompiledOutput + "\" "
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
			string[] files = Directory.GetFiles(directory, protoFileNameGlob,
												SearchOption.TopDirectoryOnly);

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
			string proto_args = "--proto_path=\"" + absProtoOutput + "\" --go_out=\"" +
								GO_absCompiledOutput + "\" "
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
