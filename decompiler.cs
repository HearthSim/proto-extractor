using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Reflection;
using System.IO;

class ProtobufDecompiler {
	public void ProcessTypes(IEnumerable<TypeDefinition> types) {
		if (types.Any(x => x.Name == "IProtoBuf" || x.Name == "ServiceDescriptor")) {
			Console.WriteLine("detected SilentOrbit protos");
			processor = new SilentOrbitTypeProcessor();
		} else {
			// We could detect based on subclassing GeneratedMessage*, but no
			// point unless we have more than 2 types of protogens.
			Console.WriteLine("assuming Google protos");
			processor = new GoogleTypeProcessor();
		}

		foreach (var type in types) {
			processor.Process(type);
		}
		processor.Complete();

		// A map from typename to message:
		var allMessages = new Dictionary<string, MessageNode>();
		// Add child nodes to their parents:
		foreach (var nodeList in processor.PackageNodes.Values) {
			var children = new List<ILanguageNode>();
			var messages = nodeList.Where(x => x is MessageNode).Select(x => x as MessageNode);
			foreach (var message in messages) {
				allMessages.Add(message.Name.Text, message);
			}
			var enums = nodeList.Where(x => x is EnumNode).Select(x => x as EnumNode);
			foreach (var node in messages.Where(n => n.Name.Name.Contains("."))) {
				var parentName = node.Name.Name;
				parentName = parentName.Substring(0, parentName.LastIndexOf('.'));
				var parent = messages.First(m => m.Name.Name == parentName);
				parent.Messages.Add(node);
				children.Add(node);
			}
			foreach (var node in enums.Where(n => n.Name.Name.Contains("."))) {
				var parentName = node.Name.Name;
				parentName = parentName.Substring(0, parentName.LastIndexOf('.'));
				var parent = messages.First(m => m.Name.Name == parentName);
				parent.Enums.Add(node);
				children.Add(node);
			}
			foreach (var node in children) {
				nodeList.Remove(node);
			}
		}

		// Move extensions to their sources:
		var blacklistedExtensionSources = new[]{
			// These types aren't allowed to have extensions, because they were
			// made by an intern or something:
			".PegasusShared.ScenarioDbRecord"
		};
		foreach (var nodeList in processor.PackageNodes.Values) {
			var messages = nodeList
				.Where(x => x is MessageNode)
				.Select(x => x as MessageNode)
				.Where(x => !blacklistedExtensionSources.Contains(x.Name.Text));
			foreach (var message in messages) {
				var extensions = message.Fields
					.Where(x => x.Label != FieldLabel.Required &&
						x.Tag >= 100 &&
						!blacklistedExtensionSources.Contains(x.TypeName.Text))
					.ToList();
				foreach (var extField in extensions) {
					message.Fields.Remove(extField);
					message.AcceptsExtensions = true;
					var target = message.Name;
					var source = extField.TypeName;
					if (String.IsNullOrEmpty(source.Package)) {
						throw new Exception("extension field is not a message");
					}
					var sourceNode = allMessages[source.Text];
					sourceNode.AddExtend(target, extField);
				}
			}
		}

		// A map from type name to its file
		var typesMap = new Dictionary<string, string>();
		var currAssembly = Assembly.GetExecutingAssembly();
		var typesMapName = currAssembly.GetManifestResourceNames()[0];
		using (var typesMapFile = currAssembly.GetManifestResourceStream(typesMapName))
		using (var typesMapReader = new StreamReader(typesMapFile)) {
			while (!typesMapReader.EndOfStream) {
				var line = typesMapReader.ReadLine().Trim();
				if (line.Length == 0) continue;
				if (line[0] == '#') continue;
				var parts = line.Split(new[]{"\t"}, StringSplitOptions.RemoveEmptyEntries);
				typesMap[parts[1]] = parts[0];
			}
		}

		foreach (var nodeList in processor.PackageNodes.Values) {
			string fileName = null;
			FileNode fileNode = null;
			foreach (var node in nodeList) {
				var type = default(TypeName);
				if (node is MessageNode) type = (node as MessageNode).Name;
				if (node is ServiceNode) type = (node as ServiceNode).Name;
				if (node is EnumNode) type = (node as EnumNode).Name;
				var typeName = type.Text;
				if (typesMap.ContainsKey(typeName)) {
					fileName = typesMap[typeName];
					if (fileNodes.ContainsKey(fileName)) {
						fileNode = fileNodes[fileName];
					} else {
						fileNode = new FileNode(fileName, type.Package);
						fileNodes[fileName] = fileNode;
					}
				} else if (fileNode == null) {
					throw new Exception(String.Format(
						"Couldn't find the first type of a package in the type map: {0}", typeName));
				}
				fileNode.Types.Add(node);
				messageFile[type.Text] = fileName;
			}
		}

		// Define "method_id" extension for identifying RPC methods.
		var methodIdExtension = new ExtendNode(new TypeName(
			"google.protobuf", "MethodOptions"));
		methodIdExtension.Fields.Add(new FieldNode(
			"method_id", FieldLabel.Optional, FieldType.UInt32, 50000));
		fileNodes["bnet/rpc"].Types.Add(methodIdExtension);
	}

	// map from filename to filenode
	Dictionary<string, FileNode> fileNodes = new Dictionary<string, FileNode>();
	// map from type to filename
	Dictionary<string, string> messageFile = new Dictionary<string, string>();

	public void WriteProtos(string extractDir = ".") {
		foreach (var pair in fileNodes) {
			var fileName = pair.Key;
			var fileNode = pair.Value;
			var files = new HashSet<string>();
			foreach (var m in fileNode.Types) {
				if (m is IImportUser) {
					foreach (var i in (m as IImportUser).GetImports()) {
						var resolvedType = i.Text;
						files.Add(
							resolvedType.StartsWith(".google.protobuf.")
							? "google/protobuf/descriptor"
							: messageFile[resolvedType]);
					}
				}
			}
			files.Remove(fileName);
			foreach (var file in files) {
				fileNode.Imports.Add(new ImportNode(file + ".proto"));
			}
			fileNode.Imports.Sort((a, b) => String.Compare(a.Target, b.Target));

			fileNode.ResolveChildren();

			var pathName = Path.Combine(extractDir, fileName + ".proto");
			Directory.CreateDirectory(Path.GetDirectoryName(pathName));
			using (var outFile = File.Create(pathName))
			using (var outStream = new StreamWriter(outFile, new UTF8Encoding(false))) {
				outStream.Write(fileNode.Text);
			}
		}
	}

	// Write protos suitable for use with golang/protobuf.  This method modifies
	// the tree.  It changes package names to match file names, and changes the
	// file structure a bit.
	public void WriteGoProtos(string outDir, string packagePrefix) {
		foreach (var pair in fileNodes) {
			var fileName = pair.Key;
			var fileNode = pair.Value;
			var package = fileName.Split('/').Last();
			packageMap[fileName] = package;
			fileNode.Package = package;
			foreach (var import in fileNode.Imports) {
				var parts = import.Target.Split('/');
				var newParts = new List<string>(parts.Take(parts.Length - 1));
				newParts.Add(parts[parts.Length - 1].Split('.').First());
				newParts.Add(parts[parts.Length - 1]);
				import.Target = packagePrefix + String.Join("/", newParts);
			}
		}
		foreach (var pair in fileNodes) {
			var fileName = pair.Key;
			var fileNode = pair.Value;
			foreach (var item in fileNode.Types) {
				var message = item as MessageNode;
				if (message == null) continue;
				RewritePackages(message);
			}

			var pathName = Path.Combine(outDir, fileName, fileName.Split('/').Last() + ".proto");
			Directory.CreateDirectory(Path.GetDirectoryName(pathName));
			using (var outFile = File.Create(pathName))
			using (var outStream = new StreamWriter(outFile, new UTF8Encoding(false))) {
				outStream.Write(fileNode.Text);
			}
		}
	}

	// map from filename to new package
	Dictionary<string, string> packageMap = new Dictionary<string, string>();

	void RewritePackages(MessageNode message) {
		foreach (var field in message.Fields) {
			var type = field.TypeName;
			if (!String.IsNullOrEmpty(type.Package)) {
				// update package:
				var baseType = String.Format(".{0}.{1}",
					type.Package,
					type.Name.Split('.').First());
				field.TypeName.Package = packageMap[messageFile[baseType]];
			}
		}
		var extends = new Dictionary<TypeName, ExtendNode>();
		foreach (var pair in message.Extends) {
			var type = pair.Key;
			var baseType = String.Format(".{0}.{1}",
				type.Package,
				type.Name.Split('.').First());
			type.Package = packageMap[messageFile[baseType]];
			extends.Add(type, pair.Value);
			foreach (var field in pair.Value.Fields) {
				type = field.TypeName;
				if (!String.IsNullOrEmpty(type.Package)) {
					// update package:
					baseType = String.Format(".{0}.{1}",
						type.Package,
						type.Name.Split('.').First());
					field.TypeName.Package = packageMap[messageFile[baseType]];
				}
			}
		}
		message.Extends = extends;
		foreach (var m in message.Messages)
			RewritePackages(m);
	}

	TypeProcessor processor;
	public ProtobufDecompiler() {
	}
}

// Represents a type capable of processing c# types generated by protoc and
// decompiling them into protobuf type nodes.
abstract class TypeProcessor {
	public abstract void Process(TypeDefinition type);

	public abstract void Complete();

	public Dictionary<string, List<ILanguageNode>> PackageNodes { get; set; }

	public TypeProcessor() {
		PackageNodes = new Dictionary<string, List<ILanguageNode>>();
	}

	protected void AddPackageNode(string package, ILanguageNode node) {
		if (!PackageNodes.ContainsKey(package)) {
			PackageNodes[package] = new List<ILanguageNode>();
		}
		PackageNodes[package].Add(node);
	}
}

// Processes protobuf types generated by SilentOrbit's protobuf implementation,
// found at <https://github.com/hultqvist/ProtoBuf>
class SilentOrbitTypeProcessor : TypeProcessor {
	public override void Process(TypeDefinition type) {
		// Since a protobuf package corresponds to a C# namespaces, no generated
		// protobuf classes exist in the root namespace.
		if (type.PackageName().Package.Length == 0) {
			return;
		}

		if (type.Interfaces.Any(r => r.Name == "IProtoBuf")) {
			ProcessMessage(type);
			// Add any nested types that are enums to enumTypes:
			AddEnumNestedTypes(type);
			return;
		}

		// These aren't actually generated by SilentOrbit's code, but they're
		// defined in protobuf.
		if (type.BaseType != null && type.BaseType.Name == "ServiceDescriptor") {
			ProcessService(type);
			return;
		}
	}

	public override void Complete() {
		foreach (var enumType in enumTypes) {
			ProcessEnum(enumType);
		}
	}

	HashSet<TypeDefinition> enumTypes = new HashSet<TypeDefinition>();
	void ProcessEnum(TypeDefinition type) {
		var typeName = type.PackageName();
		var result = new EnumNode(typeName);
		foreach (var field in type.Fields.Where(f => f.HasConstant)) {
			result.Entries.Add(Tuple.Create(field.Name, (int)field.Constant));
		}
		AddPackageNode(typeName.Package, result);
	}

	void ProcessMessage(TypeDefinition type) {
		var typeName = type.PackageName();
		var result = new MessageNode(typeName);
		var defaults = new Dictionary<string, string>();
		var deserializeWalker = new MethodWalker(type.Methods.First(m =>
			m.Name == "Deserialize" && m.Parameters.Count == 3));
		deserializeWalker.OnCall = info => {
			if (info.Conditions.Count == 0 && info.Method.Name.StartsWith("set_")) {
				var fieldName = info.Method.Name.Substring(4).ToLowerUnder();
				var val = info.Arguments[1].ToString();
				if (val.EndsWith("String::Empty")) {
					val = "\"\"";
				} else if (info.Arguments[1].GetType() == typeof(string)) {
					val = "\"" + val.Replace("\"", "\\\"") + "\"";
				}
				if (info.Method.Parameters.First().ParameterType.Name == "Boolean") {
					val = val == "0" ? "false" : "true";
				}
				defaults[fieldName] = val;
			}
		};
		deserializeWalker.Walk();

		var written = new List<byte>();
		var serializeWalker = new MethodWalker(type.Methods.First(m =>
			m.Name == "Serialize" && m.Parameters.Count == 2));
		serializeWalker.OnCall = info => {
			if (info.Method.Name == "WriteByte") {
				written.Add((byte)(int)info.Arguments[1]);
				return;
			}
			if (info.Arguments.Any(x => x.ToString().Contains("GetSerializedSize()"))) {
				return;
			}
			if (!info.Method.Name.StartsWith("Write") && info.Method.Name != "Serialize") {
				return;
			}

			// !!! packed vs not packed:
			// bnet.protocol.channel_invitation.IncrementChannelCountResponse/reservation_tokens: *not* packed
			// PegasusGame.ChooseEntities/entities: *packed*
			// not packed = {{tag, data}, {tag, data}, ...}
			// packed = {tag, size, data}
			// repeated fixed fields are packed by default.
			//
			// not packed:
			//   call: ProtocolParser.WriteUInt64(arg0, V_0)
			//   conditions: arg1.get_ReservationTokens().get_Count() > 0, &V_1.MoveNext() == true
			//
			// packed:
			//   call: ProtocolParser.WriteUInt32(arg0, V_0) // size
			//   conditions: arg1.get_Entities().get_Count() > 0, &V_2.MoveNext() == false
			//   call: ProtocolParser.WriteUInt64(arg0, V_3) // datum
			//   conditions: arg1.get_Entities().get_Count() > 0, &V_2.MoveNext() == false, &V_4.MoveNext() == true
			var iterConds = info.Conditions.Where(x => x.Lhs.Contains("MoveNext"));
			var listConds = info.Conditions.Where(x => x.Lhs.Contains("().get_Count()"));
			if (listConds.Any() && !iterConds.Any(
				x => x.Cmp == MethodWalker.Comparison.IsTrue)) {
				// Skip packed size writes:
				return;
			}
			var packed = iterConds.Any(x => x.Cmp == MethodWalker.Comparison.IsFalse);

			var label = FieldLabel.Invalid;
			if (iterConds.Any()) {
				label = FieldLabel.Repeated;
			} else if (info.Conditions.Any(x => x.Lhs.Contains(".Has"))) {
				label = FieldLabel.Optional;
			} else {
				label = FieldLabel.Required;
			}

			// Get name:
			var name = "";
			if (label == FieldLabel.Repeated) {
				name = info.Conditions.First(x => x.Lhs.Contains("get_Count()")).Lhs;
				name = name.Substring(name.IndexOf(".get_") + 5);
				name = name.Substring(0, name.Length - 14);
			} else {
				name = info.Arguments[1].ToString();
				if (name.StartsWith("Encoding.get_UTF8()")) {
					name = name.Substring(31, name.Length - 32);
				}
				name = name.Substring(name.IndexOf(".get_") + 5);
				name = name.Substring(0, name.Length - 2);
			}
			var prop = type.Properties.First(x => x.Name == name);
			name = name.ToLowerUnder();

			// Pop tag:
			var tag = 0;
			var i = 0;
			while (true) {
				var b = written[i];
				tag |= (b & 0x7f) << (7 * i);
				i += 1;
				if (0 == (b & 0x80)) break;
			}
			if (i != written.Count) {
				throw new InvalidProgramException(
					"bad tag bytes, not gonna recover from this state");
			}
			written.Clear();
			tag >>= 3;

			// Parse field type:
			var fieldType = FieldType.Invalid;
			var subType = default(TypeName);
			if (prop.PropertyType.Resolve().IsEnum) {
				fieldType = FieldType.Enum;
				var enumType = prop.PropertyType;
				enumTypes.Add(enumType.Resolve());
				subType = enumType.PackageName();
				fieldType = FieldType.Enum;
				if (defaults.ContainsKey(name)) {
					var intVal = Int32.Parse(defaults[name]);
					defaults[name] = enumType.Resolve().Fields
						.First(x => x.HasConstant && intVal == (int)x.Constant)
						.Name;
				}
			} else if (info.Method.Name == "Serialize") {
				var messageType = info.Method.DeclaringType;
				subType = messageType.PackageName();
				fieldType = FieldType.Message;
			} else if (info.Method.DeclaringType.Name == "ProtocolParser") {
				var innerType = prop.PropertyType;
				if (innerType.IsGenericInstance) {
					innerType = (innerType as GenericInstanceType).GenericArguments.First();
				}
				switch(innerType.Name) {
				// Int32, Int64,
				// UInt32, UInt64,
				// Bool, String, Bytes
				case "Int32":
					fieldType = FieldType.Int32;
					break;
				case "Int64":
					fieldType = FieldType.Int64;
					break;
				case "UInt32":
					fieldType = FieldType.UInt32;
					break;
				case "UInt64":
					fieldType = FieldType.UInt64;
					break;
				case "Boolean":
					fieldType = FieldType.Bool;
					break;
				case "String":
					fieldType = FieldType.String;
					break;
				case "Byte[]":
					fieldType = FieldType.Bytes;
					break;
				default:
					Console.WriteLine("unresolved type");
					break;
				}
			} else if (info.Method.DeclaringType.Name == "BinaryWriter") {
				// Double, Float,
				// Fixed32, Fixed64,
				// SFixed32, SFixed64,
				switch (info.Method.Parameters.First().ParameterType.Name) {
				case "Double":
					fieldType = FieldType.Double;
					break;
				case "Single":
					fieldType = FieldType.Float;
					break;
				case "UInt32":
					fieldType = FieldType.Fixed32;
					break;
				case "UInt64":
					fieldType = FieldType.Fixed64;
					break;
				default:
					Console.WriteLine("unresolved type");
					break;
				}
			}
			if (fieldType == FieldType.Invalid) {
				Console.WriteLine("unresolved type");
			}

			var field = new FieldNode(name, label, fieldType, tag);
			field.TypeName = subType;
			field.Packed = packed;
			if (defaults.ContainsKey(name)) {
				field.DefaultValue = defaults[name];
			}
			result.Fields.Add(field);
		};
		serializeWalker.Walk();

		AddPackageNode(typeName.Package, result);
	}

	void ProcessService(TypeDefinition type) {
		ServiceNode service = null;
		var retType = default(TypeName);
		var constructorWalker = new MethodWalker(type.Methods.First(m =>
			m.IsConstructor && !m.HasParameters));
		constructorWalker.OnCall = info => {
			var methodDef = info.Method.Resolve();
			if (methodDef.IsConstructor) {
				var declType = methodDef.DeclaringType;
				if (declType == type.BaseType) {
					// Base class constructor invocation.
					var fullName = info.Arguments[1] as string;
					var i = fullName.LastIndexOf('.');
					var serviceType = new TypeName(fullName.Substring(0, i), fullName.Substring(i + 1));
					service = new ServiceNode(serviceType);
				} else if (declType.FullName == "MethodDescriptor/ParseMethod") {
					var funcPtr = info.Arguments[2] as string;
					var start = funcPtr.IndexOf('<') + 1;
					var end = funcPtr.IndexOf('>');
					var fullName = funcPtr.Substring(start, end - start);
					if (!retType.Equals(default(TypeName))) {
						Console.WriteLine("Discarding RPC return type: " + retType.Name);
					}
					var i = fullName.LastIndexOf('.');
					retType = new TypeName(fullName.Substring(0, i), fullName.Substring(i + 1));
				} else if (declType.FullName == "MethodDescriptor") {
					if (retType.Equals(default(TypeName))) {
						Console.WriteLine("Missing RPC return type");
					} else {
						var fullMethodName = info.Arguments[1] as string;
						var methodName = fullMethodName.Substring(fullMethodName.LastIndexOf('.') + 1);
						var argType = new TypeName("unknown", "Unknown");
						var rpc = new RPCNode(methodName, argType, retType);
						rpc.Options.Add("(method_id)", info.Arguments[2].ToString());
						if (service != null) service.Methods.Add(rpc);
						retType = default(TypeName);
					}
				}
			}
		};
		constructorWalker.Walk();

		// TODO: Extract argument types.
		//       This will require analysis of senders, handlers and/or handler registration.

		if (service == null) {
			Console.WriteLine("Failed to extract protobuf name for service class: " + type.FullName);
		} else {
			AddPackageNode(service.Name.Package, service);
		}
	}

	void AddEnumNestedTypes(TypeDefinition type) {
		if (!type.HasNestedTypes) return;
		foreach (var subType in type.NestedTypes) {
			if (subType.IsEnum) enumTypes.Add(subType);
			AddEnumNestedTypes(subType);
		}
	}
}

// Processes protobuf types generated by the protobuf-csharp-port project,
// which is currently owned by Google and was originally created by Jon Skeet.
class GoogleTypeProcessor : TypeProcessor {
	public override void Process(TypeDefinition type) {
		return;
	}

	public override void Complete() {}
}

// Explore the code paths of a method and generate events for every possible
// call generated by the method, including data on what the call arguments are
// and what conditions were necessary to reach the call instruction.  Emits
// similar events for non-local stores.
public class MethodWalker {
	public Action<CallInfo> OnCall;
	public Action<StoreInfo> OnStore;

	public enum Comparison {
		Equal,
		Inequal,
		GreaterThan,
		GreaterThanEqual,
		IsTrue,
		IsFalse
	}

	public class Condition {
		public int Offset { get; set; }
		public string Lhs { get; set; }
		public string Rhs { get; set; }
		public Comparison Cmp { get; set; }

		public Condition(int offset, string lhs, Comparison cmp, string rhs = null) {
			Offset = offset;
			Lhs = lhs;
			Rhs = rhs;
			Cmp = cmp;
		}

		public override string ToString() {
			var cmpStr = "";
			switch (Cmp) {
			case Comparison.Equal:            cmpStr = "=="; break;
			case Comparison.Inequal:          cmpStr = "!="; break;
			case Comparison.GreaterThan:      cmpStr = ">"; break;
			case Comparison.GreaterThanEqual: cmpStr = ">="; break;
			case Comparison.IsTrue:           cmpStr = "== true"; break;
			case Comparison.IsFalse:          cmpStr = "== false"; break;
			}
			return String.Format("{0} {1}{2}", Lhs, cmpStr,
				String.IsNullOrEmpty(Rhs) ? "" : " " + Rhs);
		}
	}

	public class CallInfo {
		public List<Condition> Conditions { get; set; }
		public MethodReference Method { get; set; }
		public List<object> Arguments { get; set; }
		public string String { get; set; }
	}

	public class StoreInfo {
		public List<Condition> Conditions { get; set; }
		public FieldReference Field { get; set; }
		public string Argument { get; set; }
	}

	HashSet<int> Explored = new HashSet<int>();
	MethodDefinition Method;
	public MethodWalker(MethodDefinition method) {
		Method = method;
		Method.Body.SimplifyMacros();
	}

	class OpState {
		public int Offset { get; set; }
		public List<object> Stack { get; set; }
		public List<Condition> Conditions { get; set; }

		public OpState() {
			Offset = 0;
			Stack = new List<object>();
			Conditions = new List<Condition>();
		}

		public OpState(int offset, List<object> stack, List<Condition> conditions) {
			Offset = offset;
			Stack = stack;
			Conditions = conditions;
		}
	}

	List<OpState> processing = new List<OpState>();

	public void Walk() {
		processing.Add(new OpState());
		
		while (processing.Count > 0) {
			processing.Sort((a, b) => a.Conditions.Count - b.Conditions.Count);
			processing.Sort((a, b) => a.Offset - b.Offset);
			var next = processing.First();
			processing.Remove(next);
			// Annihilate the conditions from any branch joins:
			while (processing.Any() && processing.First().Offset == next.Offset) {
				var joinOp = processing.First();
				processing.Remove(joinOp);
				var deadConds = next.Conditions.Where(c =>
					joinOp.Conditions.Any(c2 => c2.Offset == c.Offset)).ToList();
				foreach (var c in deadConds) {
					next.Conditions.Remove(c);
				}
			}
			Explore(next.Offset, next.Stack, next.Conditions);
		}
	}

	void Explore(int offset, List<object> stack, List<Condition> conditions) {
		var ins = Method.Body.Instructions.First(o => o.Offset == offset);
		if (Explored.Contains(ins.Offset)) return;
		Explored.Add(ins.Offset);

		switch (ins.OpCode.Code) {
		case Code.Ldnull:
			stack.Add("null");
			break;
		case Code.Ldc_I4:
		case Code.Ldc_R4:
		case Code.Ldstr:
			stack.Add(ins.Operand);
			break;
		case Code.Ldloca:
			stack.Add("&" + (ins.Operand as VariableReference).ToString());
			break;
		case Code.Ldloc:
			stack.Add((ins.Operand as VariableReference).ToString());
			break;
		case Code.Ldfld:
			var field = String.Format("{0}.{1}",
				stack.Pop(), (ins.Operand as FieldReference).Name);
			stack.Add(field);
			break;
		case Code.Ldsfld:
			stack.Add((ins.Operand as FieldReference).FullName);
			break;
		case Code.Ldarg: {
			var idx = (ins.Operand as ParameterReference).Index;
			if (idx == -1) {
				stack.Add("this");
			} else {
				stack.Add(String.Format("arg{0}", idx));
			}
		} break;
		case Code.Ldelem_Ref: {
			var idx = stack.Pop();
			var arr = stack.Pop();
			stack.Add(String.Format("{0}[{1}]", arr, idx));
		} break;
		case Code.Ldftn:
			stack.Add(String.Format("&({0})", ins.Operand));
			break;
		case Code.Newobj: {
			var mr = ins.Operand as MethodReference;
			var numParam = mr.Parameters.Count;
			var stackIdx = stack.Count - numParam;
			var args = stack.GetRange(stackIdx, numParam);
			stack.RemoveRange(stackIdx, numParam);
			var callString = String.Format("new {0}({1})",
				mr.DeclaringType.Name, String.Join(", ", args));
			stack.Add(callString);
			if (OnCall == null) break;
			args.Insert(0, "this");
			OnCall(new CallInfo {
				Conditions = new List<Condition>(conditions),
				Method = mr,
				Arguments = args,
				String = callString
			});
		} break;
		case Code.Newarr:
			stack.Add(String.Format("new {0}[{1}]",
				(ins.Operand as TypeDefinition).FullName, stack.Pop()));
			break;
		case Code.Brfalse: {
			var lhs = stack.Pop().ToString();
			var src = ins.Offset;
			var tgt = (ins.Operand as Instruction).Offset;
			var cond = new Condition(src, lhs, Comparison.IsFalse);
			var ncond = new Condition(src, lhs, Comparison.IsTrue);
			Branch(tgt, stack, conditions, cond, ncond); 
		} break;
		case Code.Brtrue: {
			var lhs = stack.Pop().ToString();
			var src = ins.Offset;
			var tgt = (ins.Operand as Instruction).Offset;
			var cond = new Condition(src, lhs, Comparison.IsTrue);
			var ncond = new Condition(src, lhs, Comparison.IsFalse);
			Branch(tgt, stack, conditions, cond, ncond);
		} break;
		case Code.Beq:
		case Code.Bne_Un:
		case Code.Ble:
		case Code.Bge:
		case Code.Blt:
		case Code.Bgt: {
			var rhs = stack.Pop().ToString();
			var lhs = stack.Pop().ToString();
			var src = ins.Offset;
			var tgt = (ins.Operand as Instruction).Offset;
			Condition cond = null, ncond = null;
			switch (ins.OpCode.Code) {
			case Code.Beq:
				cond = new Condition(src, lhs, Comparison.Equal, rhs);
				ncond = new Condition(src, lhs, Comparison.Inequal, rhs);
				break;
			case Code.Bne_Un:
				cond = new Condition(src, lhs, Comparison.Inequal, rhs);
				ncond = new Condition(src, lhs, Comparison.Equal, rhs);
				break;
			case Code.Ble:
				// x <= y --> y >= x; !(x <= y) --> x > y
				cond = new Condition(src, rhs, Comparison.GreaterThanEqual, lhs);
				ncond = new Condition(src, lhs, Comparison.GreaterThan, rhs);
				break;
			case Code.Bge:
				cond = new Condition(src, lhs, Comparison.GreaterThanEqual, rhs);
				// !(x >= y) --> y > x
				ncond = new Condition(src, rhs, Comparison.GreaterThan, lhs);
				break;
			case Code.Blt:
				// x < y --> y > x; !(x < y) --> x >= y
				cond = new Condition(src, rhs, Comparison.GreaterThan, lhs);
				ncond = new Condition(src, lhs, Comparison.GreaterThanEqual, rhs);
				break;
			case Code.Bgt:
				// !(x > y) --> y >= x
				cond = new Condition(src, lhs, Comparison.GreaterThan, rhs);
				ncond = new Condition(src, rhs, Comparison.GreaterThanEqual, lhs);
				break;
			}
			Branch(tgt, stack, conditions, cond, ncond);
		} break;
		case Code.Br:
			Explore((ins.Operand as Instruction).Offset, stack, conditions);
			return;
		case Code.Stfld: {
			var arg = stack.Pop().ToString();
			/*var obj = */stack.Pop();
			if (OnStore == null) break;
			OnStore(new StoreInfo {
				Conditions = new List<Condition>(conditions),
				Field = ins.Operand as FieldReference,
				Argument = arg,
			});
		} break;
		case Code.Stelem_Ref: {
			/*var val = */stack.Pop();
			/*var idx = */stack.Pop();
			/*var arr = */stack.Pop();
		} break;
		case Code.Mul: {
			var rhs = stack.Pop().ToString();
			var lhs = stack.Pop().ToString();
			stack.Add(String.Format("{0} * {1}", lhs, rhs));
		} break;
		case Code.Call:
		case Code.Callvirt: {
			var mr = ins.Operand as MethodReference;
			var args = new List<object>();
			for (var i = 0; i < mr.Parameters.Count; i++) {
				args.Add(stack.Pop());
			}
			if (mr.HasThis) {
				args.Add(stack.Pop());
			}
			args.Reverse();
			var callString = String.Format("{0}.{1}({2})",
				mr.HasThis ? args.First().ToString() : mr.DeclaringType.Name,
				mr.Name,
				String.Join(", ",
					mr.HasThis ? args.Skip(1) : args));
			if (mr.ReturnType.FullName != "System.Void") {
				stack.Add(callString);
			}
			if (OnCall == null) break;
			OnCall(new CallInfo {
				Conditions = new List<Condition>(conditions),
				Method = mr,
				Arguments = args,
				String = callString
			});
		} break;
		}

		if (ins.Next != null) {
			processing.Add(new OpState(ins.Next.Offset, stack, conditions));
		}
	}

	void Branch(int target, List<object> stack, List<Condition> conditions,
		Condition conditionTaken, Condition conditionNotTaken) {

		var newConds = new List<Condition>(conditions);
		newConds.Add(conditionTaken);
		conditions.Add(conditionNotTaken);
		processing.Add(new OpState(target,
			new List<object>(stack), newConds));
	}
}

public static class DecompilerExtensions {
	// Because a List<T> is a more versatile stack than Stack<T>.  Mainly for
	// ease of cloning.
	public static T Pop<T>(this List<T> stack) {
		var last = stack.Count - 1;
		var result = stack[last];
		stack.RemoveAt(last);
		return result;
	}

	public static string ToLowerUnder(this string s) {
		var res = "";
		for (var i = 0; i < s.Length; i++) {
			if (s[i] >= 'A' && s[i] < 'a' && i != 0)
			{
				res += "_";
			}
			res += s[i].ToString().ToLower();
		}
		return res.TrimEnd('_');
	}

	public static TypeName PackageName(this TypeReference type) {
		var types = new List<string> { type.Name };
		while (type.DeclaringType != null) {
			type = type.DeclaringType;
			var tName = type.Name;
			if (tName == "Types") continue;
			types.Add(tName);
		}
		types.Reverse();
		return new TypeName(type.Namespace, String.Join(".", types));
	}
}
