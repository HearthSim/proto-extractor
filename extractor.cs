using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Rocks;

static class Extractor {
	static void Main(string[] args) {
		if (args.Length == 0) {
			Console.Write("Hearthstone Protobuf Extractor --- ");
			Console.WriteLine("Usage: proto-extractor [input dlls...]");
			Console.WriteLine("output is written to the working directory");
			return;
		}

		var allTypes = new List<TypeDefinition>();
		foreach (var arg in args) {
			allTypes.AddRange(ModuleDefinition.ReadModule(arg).GetAllTypes());
		}

		var decompiler = new ProtobufDecompiler();
		decompiler.ProcessTypes(allTypes);
		decompiler.WriteProtos();
	}
}
