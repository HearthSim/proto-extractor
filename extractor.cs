using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Rocks;

static class Extractor {
	static void Main(string[] args) {
		if (args.Length == 0) {
			Console.Write("Hearthstone Protobuf Extractor --- ");
			Console.WriteLine("Usage: proto-extractor [options] [input dlls...]");
			Console.WriteLine("    --output, -o  Directory where .proto files are written");
			return;
		}

		var extractDir = ".";
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
				}
			} else {
				assemblies.Add(arg);
			}
		}

		var allTypes = new List<TypeDefinition>();
		foreach (var arg in assemblies) {
			allTypes.AddRange(ModuleDefinition.ReadModule(arg).GetAllTypes());
		}

		var decompiler = new ProtobufDecompiler(extractDir);
		decompiler.ProcessTypes(allTypes);
		decompiler.WriteProtos();
	}
}
