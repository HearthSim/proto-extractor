using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

class MainClass {
	static int Main(string[] args) {
		if (args.Length == 0) {
			Console.WriteLine("USAGE: main.exe [Assembly-CSharp-firstpass.dll]");
			return 1;
		}

		var dll = File.Open(args[0], FileMode.Open, FileAccess.Read);
		var fpAssembly = AssemblyDefinition.ReadAssembly(dll);
		var module = fpAssembly.MainModule;

		foreach (var type in module.Types) {
			var id = 0;
			List<string> fieldNames = new List<string>();
			List<string> fieldTitleNames = new List<string>();
			List<string> fieldTypes = new List<string>();
			List<string> fieldRet = new List<string>();
			foreach (var subtype in type.NestedTypes) {
				if (subtype.Name == "Types") {
					foreach (var subsubtype in subtype.NestedTypes) {
						if (subsubtype.Name == "PacketID") {
							id = (int)subsubtype.Fields.First(f => f.Name == "ID").Constant;
						}
					}
				}
			}

			if (id != 0) {
				var cctor = type.Methods.First(m => m.Name == ".cctor");
				foreach (var instruction in cctor.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr)) {
					var s = instruction.Operand.ToString();
					var titleName = s.Split('_').FirstOrDefault() + String.Join("", s.Split('_').Skip(1).Select(p => p.Substring(0, 1).ToUpper() + p.Substring(1))) + "_";

					var cmd = type.Fields.First(m => m.Name == titleName);

					fieldNames.Add(s);
					fieldTitleNames.Add(titleName);
					fieldTypes.Add(cmd.FieldType.ToString());
					fieldRet.Add("('" + s + "', '" + cmd.FieldType.ToString() + "')");
				}
				Console.WriteLine("class {0}:", type.ToString().Replace(".", "_"));
				Console.WriteLine("\tID = {0}", id);
				Console.WriteLine("\tfields = ({0})", string.Join(", ", fieldRet.ToArray()));
				Console.WriteLine();
				Console.Out.Flush();
			}
		}
		return 0;
	}
}
