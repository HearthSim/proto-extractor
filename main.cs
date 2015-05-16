using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

static class MainClass {
	static int Main(string[] args) {
		if (args.Length == 0) {
			Console.WriteLine("USAGE: main.exe [Assembly-CSharp-firstpass.dll] [output directory]");
			return 1;
		}
		var outDir = ".";
		var flat = false;
		if (args.Length > 1) {
			outDir = args[1];
		}
		if (args.Length > 2) {
			flat = args[2] == "flat";
		}

		ModuleDefinition module;

		using (var dll = File.Open(args[0], FileMode.Open, FileAccess.Read)) {
			module = AssemblyDefinition.ReadAssembly(dll).MainModule;
		}

		var pbufNodes = new List<PbufNode>();

		foreach (var type in module.GetAllTypes()) {
			if (type.FullName.StartsWith("Google"))
				continue;
			if (type.FullName == "Wire")
				continue;

			if (type.IsEnum) {
				var enumNode = new PbufNode();
				enumNode.Type = "enum";
				if (type.FullName.Contains("/")) {
					var parentType = type.DeclaringType;
					while (string.IsNullOrEmpty(parentType.Namespace))
						parentType = parentType.DeclaringType;
					enumNode.Package = parentType.Namespace;
					enumNode.Name = type.FullName.Substring(1 + enumNode.Package.Length).Replace("/Types/", ".").Replace("/", ".");
				} else {
					enumNode.Package = type.Namespace;
					enumNode.Name = type.Name;
				}
				using (var sw = new StringWriter()) {
					var values = new HashSet<int>();
					var aliases = false;
					sw.WriteLine("enum {0} {{", type.Name);

					foreach (var field in type.Fields) {
						if (!field.HasConstant)
							continue;
						if (values.Contains((int)field.Constant)) {
							aliases = true;
						} else {
							values.Add((int)field.Constant);
						}
						sw.WriteLine("\t{0} = {1};", field.Name, field.Constant);
					}
					if (aliases) sw.WriteLine("\toption allow_alias = true;");

					sw.WriteLine("}");
					enumNode.Content = sw.ToString();
				}
				pbufNodes.Add(enumNode);
			} else if (type.IsClass && type.BaseType != null) {
				if (type.Interfaces.Any(i => i.Name == "IProtoBuf")) {
					var messageNode = new PbufNode();
					messageNode.Type = "message";
					if (type.FullName.Contains("/")) {
						var parentType = type.DeclaringType;
						while (string.IsNullOrEmpty(parentType.Namespace))
							parentType = parentType.DeclaringType;
						messageNode.Package = parentType.Namespace;
						messageNode.Name = type.FullName.Substring(1 + messageNode.Package.Length).Replace("/Types/", ".").Replace("/", ".");
					} else {
						messageNode.Package = type.Namespace;
						messageNode.Name = type.Name;
					}
					using (var sw = new StringWriter()) {
						sw.WriteLine("message {0} {{", type.Name);
						var fakeStack = new Stack<object>();
						// walk the constructor for default values:
						var ctor = type.Methods.First(f => f.Name == "Deserialize" && f.Parameters.Count >= 3);
						ctor.Body.SimplifyMacros();
						// field name => stringified default value
						var defaults = new Dictionary<string, string>();
						bool done = false;
						foreach (var ins in ctor.Body.Instructions) {
							if (done) break;
							object thisArg = null;
							switch (ins.OpCode.Code) {
							case Code.Ldc_I4:
							case Code.Ldc_R4:
							case Code.Ldstr:
								fakeStack.Push(ins.Operand);
								break;
							case Code.Ldloc:
								fakeStack.Push((ins.Operand as VariableReference).ToString());
								break;
							case Code.Stfld:
								var value = fakeStack.Pop();
								thisArg = fakeStack.Pop();
								var fieldName = (ins.Operand as FieldReference).Name;
								var stringified = "<Unknown type!>";
								if (value is string) {
									stringified = "\"" + ((string)value).Replace("\"", "\\\"") + "\"";
								} else {
									stringified = value.ToString();
								}
								defaults[fieldName] = stringified;
								break;
							case Code.Ldarg:
								var paramI = (ins.Operand as ParameterReference).Index;
								if (paramI == 2) {
									done = true;
									break;
								}
								fakeStack.Push(paramI == -1 ? "this" : string.Format("arg{0}", paramI));
								break;
							case Code.Ldsfld:
								var sfldRef = ins.Operand as FieldReference;
								if (sfldRef.DeclaringType.Name == "String" && sfldRef.Name == "Empty") {
									fakeStack.Push("");
								} else {
									throw new Exception("unk static field load");
								}
								break;
							case Code.Newobj:
								fakeStack.Push("new " + (ins.Operand as MethodReference).DeclaringType.FullName + "()");
								break;
							case Code.Call:
							case Code.Callvirt:
								var mr = ins.Operand as MethodReference;
								var md = mr as MethodDefinition;
								var argCount = mr.Parameters.Count;
								var argArr = new object[argCount];
								for (var i = argCount - 1; i >= 0; i--) {
									argArr[i] = fakeStack.Pop();
								}
								if (mr.HasThis) {
									thisArg = fakeStack.Pop();
								}
								if (md.SemanticsAttributes.HasFlag(MethodSemanticsAttributes.Setter)) {
									defaults[mr.Name.Substring(4).ToLowerUnderscore()] = argArr[0].ToString();
								}
								fakeStack.Push(string.Format("{0}{1}({2})", mr.HasThis ? thisArg.ToString() + "." : "", mr.Name, string.Join(", ", argArr)));
								break;
							default:
								break;
							}
						}

						// clear stack
						fakeStack = new Stack<object>();
						// walk Serialize for the remaining field information
						bool inBranch = false;
						string branchedOn = null;
						Instruction branchedUntil = null;
						var writer = type.Methods.First(m => m.Name == "Serialize" && m.Parameters.Count >= 2);
						var fieldTag = 0;
						var isMessage = false;
						var isRepeatedUint = false;
						writer.Body.SimplifyMacros();
						foreach (var ins in writer.Body.Instructions) {
							if (ins == branchedUntil) {
								inBranch = false;
								branchedOn = null;
								branchedUntil = null;
							}
							switch (ins.OpCode.Code) {
							case Code.Ldc_I4:
								fakeStack.Push(ins.Operand);
								break;
							case Code.Ldloc:
								fakeStack.Push((ins.Operand as VariableReference).ToString());
								break;
							case Code.Ldfld:
								fakeStack.Push(string.Format("{0}.{1}", fakeStack.Pop(), (ins.Operand as FieldReference).Name));
								break;
							case Code.Ldarg:
								var paramI = (ins.Operand as ParameterReference).Index;
								fakeStack.Push(paramI == -1 ? "this" : string.Format("arg{0}", paramI));
								break;
							case Code.Ldelem_Ref:
								var idx = fakeStack.Pop();
								var arr = fakeStack.Pop();
								fakeStack.Push(string.Format("{0}[{1}]", arr, idx));
								break;
							case Code.Brfalse:
								inBranch = true;
								branchedOn = fakeStack.Pop().ToString();
								branchedUntil = ins.Operand as Instruction;
								break;
							case Code.Ble:
							case Code.Bge:
							case Code.Blt:
							case Code.Bgt:
								inBranch = true;
								branchedOn = string.Format("{0} [{1}] {2}", fakeStack.Pop(), ins.OpCode.Name.Substring(1), fakeStack.Pop());
								branchedUntil = ins.Operand as Instruction;
								break;
							case Code.Call:
							case Code.Callvirt:
								var mr = ins.Operand as MethodReference;
								var argCount = mr.Parameters.Count;
								var argArr = new object[argCount];
								for (var i = argCount - 1; i >= 0; i--) {
									argArr[i] = fakeStack.Pop();
								}
								object thisArg = null;
								if (mr.HasThis) {
									thisArg = fakeStack.Pop();
								}
								fakeStack.Push(string.Format("{0}{1}({2})", mr.HasThis ? thisArg.ToString() + "." : "", mr.Name, string.Join(", ", argArr)));
								if (argCount > 0) {
									if ((mr.DeclaringType.Name == "ProtocolParser" &&
										mr.Name.StartsWith("Write"))||
										(mr.Name == "Serialize" && isMessage) ||
										mr.DeclaringType.Name == "BinaryWriter") {
										string arg1 = "";
										if (argArr.Length > 1) {
											arg1 = argArr[1].ToString();
										}
										if (arg1.EndsWith("GetSerializedSize()")) {
											isMessage = true;
											break;
										}
										else if (mr.Name != "Serialize") {
											isMessage = false;
										}
										bool optional = inBranch && branchedOn.StartsWith("arg1.Has");
										bool repeated = inBranch && branchedOn.Contains(".get_Count()");
										bool complex = false;
										var storage = "required";
										if (optional) {
											storage = "optional";
										}
										else if (repeated) {
											storage = "repeated";
										}
										string fieldName = null;
										if (arg1.StartsWith("arg1")) {
											if (arg1.Contains("get_Count()")) {
												break;
											}
											fieldName = arg1.Substring(5).Replace("()", "");
										}
										else if (repeated) {
											var enumDepth = 2;
											var stack = fakeStack.ToList();
											if (isMessage) enumDepth++;
											if (isRepeatedUint) {
												enumDepth--;
												isRepeatedUint = false;
											}
											else if (fieldTag >= 0x10) enumDepth++;
											var enumCall = stack[enumDepth].ToString();
											if (enumCall.Contains("GetEnumerator")) {
												fieldName = enumCall.Substring(5);
												fieldName = fieldName.Substring(0, fieldName.IndexOf("("));
											} else if (stack[2].ToString().StartsWith("SizeOfUInt32") && stack[4].ToString().Contains("GetEnumerator")) {
												isRepeatedUint = true;
												break;
											} else if (stack[2].ToString().StartsWith("SizeOfUInt64") && stack[4].ToString().Contains("GetEnumerator")) {
												isRepeatedUint = true;
												break;
											} else if (mr.DeclaringType.Name == "BinaryWriter") {
												fieldName = stack[1].ToString().Substring(5);
												fieldName = fieldName.Substring(0, fieldName.IndexOf("("));
											} else {
												throw new Exception();
											}
										}
										else if (arg1.Contains("get_UTF8")) {
											fieldName = arg1.Substring(25);
											fieldName = fieldName.Substring(0, fieldName.Length - 3);
										}
										else if (mr.DeclaringType.Name == "BinaryWriter") {
											fieldName = argArr[0].ToString().Substring(5).Replace("()", "");
										} else {
											throw new Exception();
										}
										var getter = type.Methods.First(m => m.Name == fieldName);
										fieldName = fieldName.Substring(4).ToLowerUnderscore();
										string protoKind = null;
										if (isMessage) {
											var messageType = getter.ReturnType;
											if (repeated) {
												messageType = ((Mono.Cecil.GenericInstanceType)messageType).GenericArguments[0];
											}
											protoKind = "." + messageType.FullName.Replace("/Types/", ".").Replace("/", ".");
											Console.WriteLine(protoKind);
											messageNode.DependencyNames.Add(protoKind);
											sw.WriteLine("\t{0} {1} {2} = {3};", storage, protoKind,
												fieldName, fieldTag);
											fieldTag = 0;
											isMessage = false;
											break;
										}
										var fieldType = getter.ReturnType;
										var kind = fieldType.Name;
										var defaultStr = "";
										protoKind = kind;
										if (kind == "List`1") {
											if (!repeated) throw new Exception();
											var subType = ((Mono.Cecil.GenericInstanceType)fieldType).GenericArguments[0];
											kind = subType.Name;
										}
										if (!fieldType.Namespace.StartsWith("System")) {
											kind = "Enum";
										}
										if (mr.DeclaringType.Name == "BinaryWriter") {
											switch (mr.Parameters[0].ParameterType.Name) {
											case "UInt32":
												protoKind = "fixed32";
												break;
											case "UInt64":
												protoKind = "fixed64";
												break;
											case "Single":
												protoKind = "float";
												break;
											case "Double":
												protoKind = "double";
												break;
											default:
												throw new Exception();
											}
										} else {
											switch (kind) {
											case "UInt32":
												protoKind = "uint32";
												break;
											case "UInt64":
												protoKind = "uint64";
												break;
											case "Single":
												protoKind = "float";
												break;
											case "Double":
												protoKind = "double";
												break;
											case "Int32":
												protoKind = "int32";
												break;
											case "Int64":
												protoKind = "int64";
												break;
											case "Boolean":
												protoKind = "bool";
												if (defaults.ContainsKey(fieldName)) {
													defaults[fieldName] = defaults[fieldName] == "0" ? "false" : "true";
												}
												break;
											case "String":
												protoKind = "string";
												break;
											case "Enum":
												protoKind = "." + getter.ReturnType.FullName.Replace("/Types/", ".").Replace("/", ".");
												messageNode.DependencyNames.Add(protoKind);
												if (defaults.ContainsKey(fieldName))
												{
													var constVal = Int32.Parse(defaults[fieldName]);
													var constName = getter.ReturnType.Resolve().Fields.First(f => f.HasConstant && constVal == (int)f.Constant).Name;
													defaults[fieldName] = constName;
												}
												break;
											case "Byte[]":
												complex = true;
												protoKind = "bytes";
												break;
											default:
												throw new Exception(string.Format("kind must be handled: {0}", kind));
											}
										}
										if (repeated) complex = true;
										if (!complex && defaults.ContainsKey(fieldName) && defaults[fieldName] != "\"\"") {
											if (protoKind == "string") {
												if (string.IsNullOrEmpty(defaults[fieldName])) {
													defaults[fieldName] = "\"\"";
												} else {
													defaults[fieldName] = string.Format("\"{0}\"", defaults[fieldName]);
												}
											}
											defaultStr = string.Format(" [default = {0}]", defaults[fieldName]);
										}
										sw.WriteLine("\t{0} {1} {2} = {3}{4};", storage, protoKind,
											fieldName, fieldTag, defaultStr);
										fieldTag = 0;
									}
									else if (mr.DeclaringType.Name == "Stream") {
										if (fieldTag != 0) {
											fieldTag &= 0x7f;
											fieldTag |= (int)argArr[0] << 4;
										} else {
											fieldTag = (int)argArr[0] >> 3;
											// fieldWireType = (int)argArr[0] & 7;
										}
									}
								}
								break;
							}
						}

						if (type.BaseType.Name == "ExtendableMessageLite`2") {
							sw.WriteLine("\textensions 100 to 10000;");
						}

						// get extensions:
						var extensions = new Dictionary<string, List<string>>();
						foreach (var extField in type.Fields.Where(
							f => f.FieldType.Name.StartsWith("GeneratedExtension"))
						) {
							var genArgs = (extField.FieldType as GenericInstanceType).GenericArguments;
							var extendedType = genArgs[0];
							var fieldType = genArgs[1];
							var extendee = "." + extendedType.FullName.Replace("/Types/", ".");
							var extFieldName = extField.Name;
							if (extFieldName.EndsWith("Extension")) {
								extFieldName = extFieldName.Substring(0, extFieldName.Length - "Extension".Length);
							}
							var fieldNameCamel = extFieldName;

							var fieldName = "" + fieldNameCamel[0];
							for (var i = 1; i < fieldNameCamel.Length; i++) {
								if (char.IsUpper(fieldNameCamel[i])) {
									fieldName += '_';
								}
								fieldName += fieldNameCamel[i];
							}
							fieldName = fieldName.ToLower();
							// note: repeated is possible, but not found currently.
							var storage = "optional";
							var protoKind = fieldType.FullName.Replace("/Types/", ".");
							fieldTag = (int)type.Fields.First(f => f.Name.StartsWith(extFieldName) && f.Name.EndsWith("FieldNumber")).Constant;
							if (!extensions.ContainsKey(extendee)) {
								extensions[extendee] = new List<string>();
								messageNode.DependencyNames.Add(extendee);
							}
							extensions[extendee].Add(string.Format("\t{0} {1} {2} = {3};",
								storage, protoKind, fieldName, fieldTag));
						}
						foreach (var pair in extensions) {
							var extendee = pair.Key;
							sw.WriteLine("\textend {0} {{", extendee);
							foreach (var field in pair.Value) {
								sw.WriteLine("\t{0}", field);
							}
							sw.WriteLine("\t}");
						}
						sw.WriteLine("}");
						messageNode.Content = sw.ToString();
					}
					pbufNodes.Add(messageNode);
				}
			}
		}
		// flatten to a single package if requested
		if (flat) {
			// Slowish
			var qnames = new Dictionary<string, string>();
			var qnamekeys = new List<string>();
			foreach (var node in pbufNodes) {
				var newName = ".hsproto." + node.Package.Replace(".", "_") + "_" + node.Name;
				qnames.Add(node.FullName, newName);
				qnamekeys.Add(node.FullName);
			}
			// Longest first
			qnamekeys.Sort((a, b) => b.Length.CompareTo(a.Length));
			foreach (var node in pbufNodes) {
				var oldName = node.Name;
				node.Name = node.Package.Replace(".", "_") + "_" + oldName;
				node.Package = "hsproto";
				node.Content = node.Content.Replace(" " + oldName, " " + node.Name);
				foreach (var key in qnamekeys) {
					node.Content = node.Content.Replace(key, qnames[key]);
				}
				node.DependencyNames = node.DependencyNames.Select(s => {
					foreach (var key in qnamekeys) {
						if (key == s)
						{
							return qnames[key];
						}
					}
					throw new Exception();
				}).ToList();
			}
		}

		// add links from outer classes to unlinked inners and uniqify depNames:
		// (linking the inners ensures they appear before/near the outers)
		foreach (var node in pbufNodes) {
			if (node.Name.Contains("."))
				continue;
			foreach (var innerNode in pbufNodes.Where(
				n => n.FullName.StartsWith(node.FullName) &&
				n != node &&
				n.FullName[node.FullName.Length] == '.')
			) {
				if (innerNode.DependencyNames.Count == 0)
					node.DependencyNames.Add(innerNode.FullName);
			}
			node.DependencyNames = node.DependencyNames.Distinct().ToList();
		}
		// create 2-way links and identify roots:
		var roots = new List<PbufNode>();
		var edges = new HashSet<Tuple<int, int>>(); // a depends on b; b points to a
		for (var i = 0; i < pbufNodes.Count; i++) {
			var node = pbufNodes[i];
			foreach (var depName in node.DependencyNames) {
				var depI = pbufNodes.FindIndex(n => n.FullName == depName);
				var depNode = pbufNodes[depI];
				edges.Add(Tuple.Create(i, depI));
				node.DependencyNodes.Add(depNode);
				depNode.DependentNodes.Add(node);
			}
			if (node.DependencyNames.Count == 0) {
				roots.Add(node);
			}
		}

		var topSorted = new List<PbufNode>();
		while (roots.Count > 0) {
			roots.Sort((a, b) => String.Compare(b.FullName, a.FullName));
			roots = roots.Distinct().ToList();
			var node = roots[roots.Count - 1];
			roots.RemoveAt(roots.Count - 1);
			topSorted.Add(node);
			var rootI = pbufNodes.FindIndex(n => n == node);
			var dependentEdges = edges.Where(e => e.Item2 == rootI).ToList();
			foreach (var edge in dependentEdges) {
				var depNode = pbufNodes[edge.Item1];
				edges.Remove(edge);
				if (edges.Count(e => e.Item1 == edge.Item1) == 0) {
					roots.Add(depNode);
				}
			}
		}
		if (edges.Count != 0) {
			// throw new Exception("cyclic dependency"); -- this actually just doesn't matter
		}
		// optimize the graph by grouping packages:
		bool madeImprovement = false;
		do { // pull up
			madeImprovement = false;
			var packagePrevs = new Dictionary<string, int>();
			for (var i = 0; i < topSorted.Count; i++) {
				var node = topSorted[i];
				foreach (var outer in node.Scopes) {
					var packagePrev = -1;
					if (packagePrevs.ContainsKey(outer)) {
						packagePrev = packagePrevs[outer];
					}
					packagePrevs[outer] = i;
					if (packagePrev < 0 || i - packagePrev == 1) {
						continue;
					}
					var canMove = true;
					for (var j = packagePrev + 1; j < i; j++) {
						var prevNode = topSorted[j];
						if (node.DependencyNodes.Contains(prevNode)) {
							canMove = false;
							break;
						}
					}
					if (canMove) {
						madeImprovement = true;
						var tmpNode = node;
						for (var j = packagePrev + 1; j < i; j++) {
							var tmp = topSorted[j];
							topSorted[j] = tmpNode;
							tmpNode = tmp;
						}
						topSorted[i] = tmpNode;
						break;
					}
				}
				if (madeImprovement) {
					break;
				}
			}
		} while (madeImprovement);
		do { // pull down
			madeImprovement = false;
			var packagePrevs = new Dictionary<string, int>();
			for (var i = topSorted.Count - 1; i >= 0; i--) {
				var node = topSorted[i];
				foreach (var outer in node.Scopes) {
					var packagePrev = -1;
					if (packagePrevs.ContainsKey(outer)) {
						packagePrev = packagePrevs[outer];
					}
					packagePrevs[outer] = i;
					if (packagePrev < 0 || packagePrev - i == 1) {
						continue;
					}
					var canMove = true;
					for (var j = packagePrev - 1; j > i; j--) {
						var prevNode = topSorted[j];
						if (prevNode.DependencyNodes.Contains(node)) {
							canMove = false;
							break;
						}
					}
					if (canMove) {
						madeImprovement = true;
						var tmpNode = node;
						for (var j = packagePrev - 1; j > i; j--) {
							var tmp = topSorted[j];
							topSorted[j] = tmpNode;
							tmpNode = tmp;
						}
						topSorted[i] = tmpNode;
						break;
					}
				}
				if (madeImprovement) {
					break;
				}
			}
		} while (madeImprovement);

		var currPackage = "";
		StringWriter currWriter = null;
		HashSet<string> currExports = null;
		HashSet<string> currImports = null;
		var packageWriters = new Dictionary<string, List<StringWriter>>();
		var packageOrder = new List<string>();
		var packageExports = new List<HashSet<string>>();
		var packageImports = new List<HashSet<string>>();
		foreach (var node in topSorted) {
			if (node.Package != currPackage) {
				if (currImports != null)
					currImports.RemoveWhere(currExports.Contains);
				currExports = new HashSet<string>();
				packageExports.Add(currExports);
				currImports = new HashSet<string>();
				packageImports.Add(currImports);
				currPackage = node.Package;
				packageOrder.Add(currPackage);
				currWriter = new StringWriter();
				if (!packageWriters.ContainsKey(currPackage)) {
					packageWriters[currPackage] = new List<StringWriter>();
				}
				packageWriters[currPackage].Add(currWriter);

				currWriter.WriteLine("syntax = \"proto2\";");
				currWriter.WriteLine("package {0};", currPackage);
				currWriter.WriteLine("<<IMPORTS HEADER>>");
				currWriter.WriteLine();
			}

			if (node.Name.Contains(".")) {
				continue;
			}

			Func<PbufNode, string> fullContent = null;
			fullContent = (currNode) => {
				currExports.Add(currNode.FullName);
				foreach (var n in currNode.DependencyNames)
					if (!currImports.Contains(n))
						currImports.Add(n);
				var content = currNode.Content;
				var insertI = 1 + content.IndexOf("\n");
				var directInnerTypes = topSorted.Where(n =>
					n != currNode &&
					n.FullName.StartsWith(currNode.FullName) &&
					n.FullName[currNode.FullName.Length] == '.' &&
					n.FullName.IndexOf(".", currNode.FullName.Length + 1) < 0
				).ToList();
				var insertion = "";
				foreach (var n in directInnerTypes) {
					var innerContent = "\t" + fullContent(n).Replace("\n", "\n\t");
					insertion += innerContent.Substring(0, innerContent.Length - 1);
				}
				return content.Substring(0, insertI) + insertion + content.Substring(insertI);
			};
			currWriter.WriteLine(fullContent(node));
		}

		var packageIs = packageWriters.Keys.ToDictionary(a => a, a => 0);
		var packagePaths = new List<string>();
		for (var i = 0; i < packageOrder.Count; i++) {
			var package = packageOrder[i];
			var importList = packageImports[i];
			var currI = packageIs[package];
			packageIs[package]++;
			var writers = packageWriters[package];
			string currPath = null;
			if (currI > 0 || writers.Count > 1) {
				currPath = string.Format("{0}_{1}.proto", package.Replace('.', '/'), currI);
			} else {
				currPath = string.Format("{0}.proto", package.Replace('.', '/'));
			}
			packagePaths.Add(currPath);
			var writer = writers[0];
			writers.Remove(writer);
			var buf = writer.ToString();
			using (var imports = new StringWriter()) {
				var importPaths = new HashSet<string>();
				foreach (var importName in importList) {
					for (var j = 0; j < packagePaths.Count - 1; j++) {
						var packagePath = packagePaths[j];
						var exportList = packageExports[j];
						if (exportList.Contains(importName) && !importPaths.Contains(packagePath))
							importPaths.Add(packagePath);
					}
				}
				foreach (var path in importPaths) {
					imports.WriteLine("import \"{0}\";", path);
				}

				buf = buf.Replace("<<IMPORTS HEADER>>", imports.ToString());
			}

			var fullPath = Path.Combine(outDir, currPath);
			var dirName = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
				Directory.CreateDirectory(dirName);
			File.WriteAllText(fullPath, buf);
		}
		return 0;
	}

	class PbufNode {
		public PbufNode() {}

		public List<string> DependencyNames = new List<string>();
		public List<PbufNode> DependencyNodes = new List<PbufNode>();
		public List<PbufNode> DependentNodes = new List<PbufNode>();

		public string Type;
		public string Package;
		public string Name;
		public string FullName { get { return "." + Package + "." + Name; } }
		public string Content;

		public IEnumerable<string> Scopes {
			get {
				var fName = FullName;
				var i = 1 + Package.Length;
				while (i != -1) {
					yield return fName.Substring(0, i);
					i = fName.IndexOf(".", i + 1);
				}
				yield return fName;
			}
		}

		public override string ToString()
		{
			return FullName;
		}

		public override int GetHashCode()
		{
			return FullName.GetHashCode();
		}
	}
}

static class Extensions {
	public static string ToLowerUnderscore(this string s) {
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
}
