using System;
using System.Collections;
using System.Collections.Generic;

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
			// TODO: optimize away full-qualification
			return String.Format(".{0}.{1}", Package, Name);
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
	}

	public string Package;
	public string Name;

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
			var result = "";
			foreach (var i in Imports) result += i.Text;
			result += "\n";
			foreach (var m in Messages) result += m.Text + "\n";
			return result;
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent = null) {
		Parent = parent;
		foreach (var i in Imports) i.ResolveChildren(this);
		foreach (var m in Messages) m.ResolveChildren(this);
	}

	public List<ImportNode> Imports;
	public List<MessageNode> Messages;
	public string Package;

	public FileNode(string package) {
		Package = package;
		Imports = new List<ImportNode>();
		Messages = new List<MessageNode>();
	}
}

// Imports a package.  The notion of weak/public import isn't supported.
public class ImportNode : ILanguageNode {
	public string Text {
		get {
			return String.Format("import {0};\n", Target);
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
public class MessageNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("message {0} {{\n", Name);

			foreach (var e in Enums)
				foreach (var line in e.Text.Split('\n'))
					result += "\t" + line + "\n";
			foreach (var m in Messages)
				foreach (var line in m.Text.Split('\n'))
					result += "\t" + line + "\n";
			foreach (var f in Fields)
				result += "\t" + f.Text;

			return result + "}\n";
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		foreach (var e in Enums) e.ResolveChildren(this);
		foreach (var m in Messages) m.ResolveChildren(this);
		foreach (var f in Fields) f.ResolveChildren(this);
	}

	public List<EnumNode> Enums;
	public List<MessageNode> Messages;
	public List<FieldNode> Fields;
	public string Name;

	public MessageNode(string name) {
		Name = name;
		Enums = new List<EnumNode>();
		Messages = new List<MessageNode>();
		Fields = new List<FieldNode>();
	}
}

// EnumNode also doesn't support options.
public class EnumNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("enum {0} {{\n", Name);
			foreach (var pair in Entries) {
				result += String.Format("{0} = {1};\n", pair.Key, pair.Value);
			}
			return result + "}\n";
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
	}

	public Dictionary<string, int> Entries;
	public string Name;

	public EnumNode(string name) {
		Name = name;
		Entries = new Dictionary<string, int>();
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

// An RPC service.  Not used.
public class ServiceNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("service {0} {\n", Name);
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

	public string Name;
	public List<RPCNode> Methods;

	public ServiceNode(string name) {
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
