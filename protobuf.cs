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

public interface IImportUser {
	List<TypeName> GetImports();
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
	public TypeName OuterType {
		get {
			if (Name == null)
				return this;

			var i = Name.IndexOf('.');
			return i < 0 ? this : new TypeName(Package, Name.Substring(0, i));
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

// Only supports messages, extensions, fields, and enums within the message.
// This leaves out options, reservations, and groups.
[DebuggerDisplay("<MessageNode Name={Name.Text}>")]
public class MessageNode : ILanguageNode, IImportUser {
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
			foreach (var extend in Extends.Values)
				foreach (var line in extend.Text.Split('\n'))
					if (line.Trim().Length != 0)
						result += "\t" + line + "\n";
			if (AcceptsExtensions)
				result += "\textensions "
					+ (FieldUpperBound < 100? 100 : ExtendLowerBound) + " to "
					+ (FieldUpperBound > ExtendUpperBound? ExtendUpperBound : 10000) + ";\n";

			return result + "}\n";
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		foreach (var e in Enums) e.ResolveChildren(this);
		foreach (var m in Messages) m.ResolveChildren(this);
		foreach (var f in Fields) f.ResolveChildren(this);
		foreach (var e in Extends.Values) e.ResolveChildren(this);
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
		foreach (var e in Extends.Values)
			foreach (var i in e.GetImports())
				result.Add(i);
		return result
			.Where(x => !alreadyHere.Contains(x))
			// Lop off any subtyping, because we only care about the containing message:
			.Select(x => x.OuterType)
			.ToList();
	}

	public List<EnumNode> Enums;
	public List<MessageNode> Messages;
	public List<FieldNode> Fields;
	public Dictionary<TypeName, ExtendNode> Extends;
	public TypeName Name;
	public bool AcceptsExtensions;
	public int ExtendLowerBound;
	public int ExtendUpperBound;

	public int FieldUpperBound {
		get {
			return Fields.Max(x => x.Tag);
		}
	}

	public MessageNode(TypeName name) {
		Name = name;
		Enums = new List<EnumNode>();
		Messages = new List<MessageNode>();
		Fields = new List<FieldNode>();
		Extends = new Dictionary<TypeName, ExtendNode>();
		ExtendLowerBound = int.MaxValue;
		ExtendUpperBound = int.MinValue;
	}

	public void AddExtend(TypeName target, FieldNode field) {
		if (!Extends.ContainsKey(target))
			Extends[target] = new ExtendNode(target);
		Extends[target].Fields.Add(field);
	}
}

// EnumNode also doesn't support options.
public class EnumNode : ILanguageNode {
	public string Text {
		get {
			var result = String.Format("enum {0} {{\n", Name.Final);
			if (HasAliases)
				result += "\toption allow_alias = true;\n";
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

	public bool HasAliases
	{
		get
		{
			return Entries.Select(x => x.Item2).GroupBy(x => x).OrderByDescending(x => x.Count()).FirstOrDefault().Count() > 1;
		}
	}

	public List<Tuple<string, int>> Entries;
	public TypeName Name;

	public EnumNode(TypeName name) {
		Name = name;
		Entries = new List<Tuple<string, int>>();
	}
}

// A customization by extension.
public class ExtendNode : ILanguageNode, IImportUser {
	public string Text {
		get {
			var result = String.Format("extend {0} {{\n", Target.Text);
			foreach (var field in Fields)
				result += "\t" + field.Text;
			result += "}\n";
			return result;
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent = null) {
		Parent = parent;
		// Pass parent since the extension itself is not a scope.
		Target.ResolveChildren(parent);
		foreach (var f in Fields) f.ResolveChildren(parent);
	}

	public List<TypeName> GetImports() {
		var result = new HashSet<TypeName>();
		result.Add(Target);
		foreach (var f in Fields)
			if (f.Type == FieldType.Message || f.Type == FieldType.Enum)
				result.Add(f.TypeName);
		return result.Select(x => x.OuterType).ToList();
	}

	public TypeName Target;
	public List<FieldNode> Fields;

	public ExtendNode(TypeName target) {
		Target = target;
		Fields = new List<FieldNode>();
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
public class ServiceNode : ILanguageNode, IImportUser {
	public string Text {
		get {
			var result = String.Format("service {0} {{\n", Name.Name);
			foreach (var method in Methods) {
				foreach (var line in method.Text.Split('\n'))
					if (line.Trim().Length != 0)
						result += "\t" + line + "\n";
			}
			result += "}\n";
			return result;
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		Name.ResolveChildren(this);
		foreach (var m in Methods) m.ResolveChildren(this);

		// Make names unique.
		// For some reason bnet.protocol.channel.Channel has two AddMember methods.
		Dictionary<string, int> nameCount = new Dictionary<string, int>();
		foreach (var m in Methods) {
			int count;
			nameCount.TryGetValue(m.Name, out count);
			count += 1;
			nameCount[m.Name] = count;
			if (count > 1) {
				m.Name += count.ToString();
			}
		}
	}

	public List<TypeName> GetImports() {
		var result = new HashSet<TypeName>();
		foreach (var m in Methods) {
			foreach (var i in m.GetImports()) {
				result.Add(i);
			}
		}
		return result.Select(x => x.OuterType).ToList();
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
			var result = String.Format("rpc {0} ({1}) returns ({2})",
				Name, RequestTypeName.Text, ResponseTypeName.Text);
			if (Options.Count == 0) {
				result += ";\n";
			} else {
				result += " {\n";
				foreach (var item in Options) {
					result += String.Format("\toption {0} = {1};\n", item.Key, item.Value);
				}
				result += "}\n";
			}
			return result;
		}
	}

	public ILanguageNode Parent { get; private set; }

	public void ResolveChildren(ILanguageNode parent) {
		Parent = parent;
		RequestTypeName.ResolveChildren(this);
		ResponseTypeName.ResolveChildren(this);
	}

	public List<TypeName> GetImports() {
		var result = new HashSet<TypeName>();
		result.Add(RequestTypeName);
		result.Add(ResponseTypeName);
		return result.Select(x => x.OuterType).ToList();
	}

	public string Name;
	public TypeName RequestTypeName;
	public TypeName ResponseTypeName;
	public IDictionary<string, string> Options;

	public RPCNode(string name, TypeName requestName, TypeName responseName) {
		Name = name;
		// TODO: Extracting the actual argument type is not implemented yet.
		//       For now put a valid type here to please the protoc compiler.
		//RequestTypeName = requestName;
		RequestTypeName = new TypeName("bnet.protocol", "NoData");
		ResponseTypeName = responseName;
		Options = new SortedDictionary<string, string>();
	}
}
