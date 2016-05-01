using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Rocks;

static class Extractor {
	static void Main(string[] args) {
		if (args.Length < 2) {
			Console.WriteLine("Hearthstone Protobuf Extractor --- ");
			Console.WriteLine("Usage: proto-extractor [options] [types file] [input dlls...]");
			Console.WriteLine("    --output, -o  Directory where .proto files are written");
			Console.WriteLine("    --go-out, -g  Directory where Go-friendly .proto files are written");
			return;
		}

		var typesFile = "";
		var extractDir = ".";
		string goDir = null;
		var assemblies = new List<string>();
		for (var i = 0; i < args.Length; i++) {
			var arg = args[i];
			if (arg[0] == '-') {
				if (arg.StartsWith("--output") || arg.StartsWith("-o")) {
					if (arg.Contains("=")) {
						extractDir = arg.Split('=')[1];
					} else {
						extractDir = args[i + 1];
						i += 1;
					}
				} else if (arg.StartsWith("--go_out") || arg.StartsWith("-g")) {
					if (arg.Contains("=")) {
						goDir = arg.Split('=')[1];
					} else {
						goDir = args[i + 1];
						i += 1;
					}
				}
			} else {
				if (typesFile.Length == 0)
					typesFile = arg;
				else
					assemblies.Add(arg);
			}
		}

		if (typesFile == "") {
			Console.WriteLine("No types file specified");
			return;
		}

		if (assemblies.Count == 0) {
			Console.WriteLine("No DLL files specified");
			return;
		}

		var allTypes = new List<TypeDefinition>();
		foreach (var arg in assemblies) {
			allTypes.AddRange(ModuleDefinition.ReadModule(arg).GetAllTypes());
		}

		var decompiler = new ProtobufDecompiler();
		decompiler.ProcessTypes(allTypes, typesFile);
		decompiler.WriteProtos(extractDir);
		if (goDir != null) {
			decompiler.WriteGoProtos(goDir, "github.com/HearthSim/hs-proto-go/");
		}
	}
}
