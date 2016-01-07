using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

// A protobuf AST node.  These nodes are a subset of the full protobuf language,
public interface ILanguageNode {
	// The source code representation of the node.  Should end with a newline.
	string Text { get; }

	ILanguageNode Parent { get; }

	// Assign each child's Parent recursively.
	void ResolveChildren(ILanguageNode parent);
}

// A complete reference to a type.  This is a struct because it should be easily
// copied.
public struct TypeName : ILanguageNode {
	public string Text {
		get {
			var needPackage = false;
			var package = "." + Package;
			if (Parent == null) {
				needPackage = true;
			} else {
				var parentFile = Parent;
				while (!(parentFile is FileNode)) parentFile = parentFile.Parent;
				var filePackage = "." + (parentFile as FileNode).Package;
				needPackage = package != filePackage;
				if (needPackage) {
					var fileParts = filePackage.Split('.');
					var myParts = package.Split('.');
					var i = 0;
					for (; i < myParts.Length && i < fileParts.Length; i++) {
						if (myParts[i] != fileParts[i]) break;
					}
					package = String.Join(".", myParts.Skip(i));
				}
			}
			if (needPackage) {
				if (!String.IsNullOrEmpty(package)) package += ".";
				return String.Format("{0}{1}", package, Name);
			} else {
				var parent = Parent;
				var names = Name.Split('.');
				while (names.Length > 1 && parent != null) {
					if (parent is MessageNode &&
						(parent as MessageNode).Name.Name == names[0]) {
						names = names.Skip(1).ToArray();
						parent = Parent;
					} else {
						parent = parent.Parent;
					}
				}
				return String.Join(".", names);
			}
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
	}

	public string Package;
	public string Name;
	public string Final {
		get {
			return Name.Split('.').Last();
		}
	}

	public TypeName(string package, string name) : this() {
		Parent = null;
		Package = package;
		Name = name;
	}
}

// A .proto file containing imports and messages.
public class FileNode : ILanguageNode {
	public string Text {
		get {
			var result = new List<string>();
			result.Add(String.Format("package {0};", Package));
			if (Imports.Any())
				result.Add(String.Join("", Imports.Select(x => x.Text)));
			result.Add("");
			foreach (var m in Types) result.Add(m.Text);
			return String.Join("\n", result);
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent = null) {
		Parent = parent;
		foreach (var i in Imports) i.ResolveChildren(this);
		foreach (var m in Types) m.ResolveChildren(this);
	}

	public List<ImportNode> Imports;
	public List<ILanguageNode> Types;
	public string Name;
	public string Package;

	public FileNode(string name, string package) {
		Name = name;
		Package = package;
		Imports = new List<ImportNode>();
		Types = new List<ILanguageNode>();
	}
}

// Imports a package.  The notion of weak/public import isn't supported.
public class ImportNode : ILanguageNode {
	public string Text {
		get {
			return String.Format("import \"{0}\";\n", Target);
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
	}

	public string Target;

	public ImportNode(string target) {
		Target = target;
	}
}

// Only supports messages, fields, and enums within the message.  This leaves
// out options, extensions, reservations, and groups.
[DebuggerDisplay("<MessageNode Name={Name.Text}>")]
public class MessageNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("message {0} {{\n", Name.Final);

			foreach (var e in Enums)
				foreach (var line in e.Text.Split('\n'))
					if (line.Trim().Length != 0)
						result += "\t" + line + "\n";
			foreach (var m in Messages)
				foreach (var line in m.Text.Split('\n'))
					if (line.Trim().Length != 0)
						result += "\t" + line + "\n";
			if ((Enums.Any() || Messages.Any()) && Fields.Any())
				result += "\n";
			foreach (var f in Fields)
				result += "\t" + f.Text;
			foreach (var pair in Extends) {
				result += String.Format("\textend {0} {{\n", pair.Key.Text);
				foreach (var field in pair.Value)
					result += "\t\t" + field.Text;
				result += "\t}\n";
			}
			if (AcceptsExtensions)
				result += "\textensions 100 to 10000;\n";

			return result + "}\n";
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		foreach (var e in Enums) e.ResolveChildren(this);
		foreach (var m in Messages) m.ResolveChildren(this);
		foreach (var f in Fields) f.ResolveChildren(this);
		foreach (var name in Extends.Keys) name.ResolveChildren(this);
		foreach (var fields in Extends.Values)
			foreach (var f in fields)
				f.ResolveChildren(this);
	}

	public List<TypeName> GetImports() {
		var result = new HashSet<TypeName>();
		var alreadyHere = new HashSet<TypeName>();
		foreach (var e in Enums) alreadyHere.Add(e.Name);
		foreach (var m in Messages) {
			alreadyHere.Add(m.Name);
			foreach (var i in m.GetImports())
				result.Add(i);
		}
		foreach (var f in Fields)
			if (f.Type == FieldType.Message || f.Type == FieldType.Enum)
				result.Add(f.TypeName);
		foreach (var e in Extends.Keys) result.Add(e);
		return result
			.Where(x => !alreadyHere.Contains(x))
			// Lop off any subtyping, because we only care about the containing message:
			.Select(x => {
				var i = x.Name.IndexOf(".");
				if (i < 0) return x;
				return new TypeName(x.Package, x.Name.Substring(0, i));
			})
			.ToList();
	}

	public List<EnumNode> Enums;
	public List<MessageNode> Messages;
	public List<FieldNode> Fields;
	public Dictionary<TypeName, List<FieldNode>> Extends;
	public TypeName Name;
	public bool AcceptsExtensions;

	public MessageNode(TypeName name) {
		Name = name;
		Enums = new List<EnumNode>();
		Messages = new List<MessageNode>();
		Fields = new List<FieldNode>();
		Extends = new Dictionary<TypeName, List<FieldNode>>();
	}

	public void AddExtend(TypeName target, FieldNode field) {
		if (!Extends.ContainsKey(target))
			Extends[target] = new List<FieldNode>();
		Extends[target].Add(field);
	}
}

// EnumNode also doesn't support options.
public class EnumNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("enum {0} {{\n", Name.Final);
			foreach (var tuple in Entries) {
				result += String.Format("\t{0} = {1};\n", tuple.Item1, tuple.Item2);
			}
			return result + "}\n";
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
	}

	public List<Tuple<string, int>> Entries;
	public TypeName Name;

	public EnumNode(TypeName name) {
		Name = name;
		Entries = new List<Tuple<string, int>>();
	}
}

public enum FieldLabel {
	Invalid,
	Required,
	Optional,
	Repeated
}

public enum FieldType {
	Invalid,
	Double, Float,
	Int32, Int64,
	UInt32, UInt64,
	SInt32, SInt64,
	Fixed32, Fixed64,
	SFixed32, SFixed64,
	Bool,
	String, Bytes,
	Message, Enum
}

// Only "Normal" fields are supported.
public class FieldNode : ILanguageNode {
	public string Text {
		get {
			var label = Enum.GetName(typeof(FieldLabel), Label).ToLower();
			var type = Enum.GetName(typeof(FieldType), Type).ToLower();
			if (Type == FieldType.Message || Type == FieldType.Enum) {
				type = TypeName.Text;
			}
			var result = String.Format("{0} {1} {2} = {3}",
				label, type, Name, Tag);
			if (DefaultValue != null) {
				result += String.Format(" [default = {0}]", DefaultValue);
			}
			if (Packed) {
				result += " [packed = true]";
			}
			return result + ";\n";
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		TypeName.ResolveChildren(this);
	}

	public FieldLabel Label;
	public FieldType Type;
	public string Name;
	public int Tag;
	// used when Type is Enum or Message
	public TypeName TypeName;
	// [default = this], shown if not null
	public string DefaultValue;
	// shown if not false
	public bool Packed;

	public FieldNode(string name, FieldLabel label, FieldType type, int tag) {
		Label = label;
		Type = type;
		Name = name;
		Tag = tag;
	}
}

// An RPC service.
public class ServiceNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("service {0} {{\n", Name.Name);
			foreach (var method in Methods) {
				result += method.Text;
			}
			result += "}\n";
			return result;
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
	}

	public TypeName Name;
	public List<RPCNode> Methods;

	public ServiceNode(TypeName name) {
		Name = name;
		Methods = new List<RPCNode>();
	}
}

public class RPCNode : ILanguageNode {
	public string Text {
		get {
			return String.Format("rpc {0} ({1}) returns ({2});\n",
				Name, RequestTypeName, ResponseTypeName);
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		RequestTypeName.ResolveChildren(this);
		ResponseTypeName.ResolveChildren(this);
	}

	public string Name;
	public TypeName RequestTypeName;
	public TypeName ResponseTypeName;

	public RPCNode(string name, TypeName requestName, TypeName responseName) {
		Name = name;
		RequestTypeName = requestName;
		ResponseTypeName = responseName;
	}
}
