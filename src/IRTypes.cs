using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace protoextractor.IR
{
	/*
	    Namespace for intermediate programming language structures/types.
	    Like any modern compiler, the project is split in a front- and backend.

	    The frontend handles the parsing of incoming data, while producing a universal
	    intermediate structure of that data: the Intermediate Representation.

	    The backend takes this intermediate representation and produces the desired
	    output.
	*/


	[DebuggerDisplay("IRProgram")]
	public class IRProgram
	{
		/*
		    Represents a complete program.
		*/

		public List<IRNamespace> Namespaces;
	}

	public class IRProgramNode
	{
		/*
		    Every IR object should be usable in a flexible way.
		    This class guarantees certain properties are always set.
		*/

		// The original name is the fullname given to this node on creation.
		// It's immutable and provided as ground truth variable.
		public string OriginalName
		{
			get;
		}

		// See IRNamespace for correct syntax of this content.
		public string FullName
		{
			get;
			set;
		}
		public string ShortName
		{
			get;
			set;
		}
		// Indicates if this IR object is a nested (private) type. Defaults to false.
		public bool IsPrivate
		{
			get;
			set;
		}
		// The IR parent object for this one.
		// Only namespaces are expected to NOT have a parent reference set.
		public IRProgramNode Parent
		{
			get;
			set;
		}

		// Constructor forces these properties to be set at instance creation.
		public IRProgramNode(string _fullName, string _shortName)
		{
			if (_fullName == null || _fullName.Length == 0)
			{
				throw new System.Exception("A non-empty fullname must be provided!");
			}

			if (_shortName == null || _shortName.Length == 0)
			{
				_shortName = ProcessShortName(_fullName);
			}

			OriginalName = _fullName;
			FullName = _fullName;
			ShortName = _shortName;
			IsPrivate = false;
			Parent = null;
		}

		/*
		    This method processes the given fullname and returns a shortname.
		    This method basically explodes the fullname on the DOT character and returns the
		    last part.
		*/
		public static string ProcessShortName(string fullName)
		{
			var nameParts = fullName.Split('.');
			return nameParts.Last();
		}

		public override string ToString()
		{
			// Debatable if this should be fullName or originalName.
			return OriginalName;
		}
	}

	[DebuggerDisplay("IRNamespace {FullName}")]
	public class IRNamespace : IRProgramNode
	{
		/*
		    A namespace is a container for multiple Classes and Enums. The namespace
		    acts as a parent to these other objects.
		    A namespace represents one package in protobuffer terms.
		    Namespaces starting with the same sequence of characters have a parent-child
		    relation in a sense that one namespace can act as a parent of another.
		    It's important to have no nameclashes within one namespace, but also
		    between types and possible nested namespaces!

		    Namespaces map directly onto proto packages!

		    The fullname of a namespace consists of one or multiple character sequences
		    joined by a DOT character.
		    The DOT character indicates a subnamespace.

		    eg; toplevel_namespace.sub_namespace.subsubnamespace

		    The shortname is the last character sequence part of the fullname.
		    In case of the previous example, it's subsubnamespace.

		    Namespace names are ALWAYS lowercased!
		*/

		// All classes found directly under this namespace.
		public List<IRClass> Classes;
		// All enums found directly inder this namespace.
		public List<IREnum> Enums;

		// Do not allow others to change the IsPrivate flag, since namespaces cannot be private!
		public new bool IsPrivate
		{
			get;
		}

		// Do not allow others to change the Parent, since a namespace cannot have a parent!
		public new IRProgramNode Parent
		{
			get;
		}

		public IRNamespace(string _fullName, string _shortName) : base(_fullName, _shortName)
		{
			Classes = new List<IRClass>();
			Enums = new List<IREnum>();
		}
	}

	public class IRTypeNode : IRProgramNode
	{
		/*
		    A space which contains all types a namespace can hold, currently class and enum.
		    By inheriting this class, types are forced to have a fullname and shortname!

		    The shortname is the actual name of the type object.
		    eg: PROPERTY_KIND

		    The fullname is a concatenation of the parent namespace's fullname and the
		    shortname of this type. The joining element is the DOT character.
		    eg: parent_namespace.PROPERTY_KIND
		*/

		public IRTypeNode(string _fullName, string _shortName) : base(_fullName, _shortName) { }
	}

	[DebuggerDisplay("IRClass {FullName}")]
	public class IRClass : IRTypeNode
	{
		/*
		    Container of a set of properties.

		    Classes map directly to proto messages!

		    Class names are always in PscalCase!
		    Class property names are always in snake_case!
		*/

		// All properties of this class.
		public List<IRClassProperty> Properties;
		// All types that are only referenceble by properties of this
		// specific class.
		public List<IRTypeNode> PrivateTypes;

		public IRClass(string _fullName, string _shortName) : base(_fullName, _shortName)
		{
			Properties = new List<IRClassProperty>();
			PrivateTypes = new List<IRTypeNode>();
		}
	}

	[DebuggerDisplay("IREnum {FullName}")]
	public class IREnum : IRTypeNode
	{
		/*
		    Enums give semantic value to integers.

		    Enums map directly to proto enums!

		    Eums names are always in PascalCase!
		    Enum property names are always in UPPERCASE!
		*/

		// All properties of this enum.
		public List<IREnumProperty> Properties;

		public IREnum(string _fullName, string _shortName) : base(_fullName, _shortName)
		{
			Properties = new List<IREnumProperty>();
		}
	}

	[DebuggerDisplay("IRProperty {Type} -- {Name}")]
	public class IRClassProperty
	{

		public class ILPropertyOptions
		{
			/*
			    We must have some explicit details about properties regarding
			    protobuffer, because this data cannot be guessed if not extracted
			    directly from the sources.
			    See protobuffer manual for more information about the options.
			*/

			public FieldLabel Label;
			public int PropertyOrder;
			public bool IsPacked;
			public string DefaultValue;
		}

		public string Name;
		// Actual semantic meaning of data that needs to be stored behind this property.
		public PropertyTypeKind Type;
		// If 'Type' is a reference to something else, 'ReferencedType' will hold the actual
		// (or placeholder) object that IS the actual referenced object.
		// Placeholder is ALLOWED because FullName and ShortName will be set anyhow and
		// can be used for searching the original type.
		public IRTypeNode ReferencedType;
		// The actual options for this property
		public ILPropertyOptions Options;
	}

	[DebuggerDisplay("IRProperty {Name} with value {Value}")]
	public class IREnumProperty
	{
		public string Name;
		public int Value;
	}

	// DO NOT CHANGE THE ORDER OF THE PROPERTIES!
	public enum PropertyTypeKind
	{
		UNKNOWN = 0,
		DOUBLE,
		FLOAT,

		INT32,
		INT64,
		UINT32,
		UINT64,
		SINT32,
		SINT64,

		FIXED32,
		FIXED64,
		SFIXED32,
		SFIXED64,

		BOOL,
		STRING,
		BYTES,
		// Reference to other type
		TYPE_REF
	}

	public enum FieldLabel
	{
		INVALID = 0,
		OPTIONAL,
		REQUIRED,
		REPEATED
	}

	/* HELPER functionality */
	static class IRTypeExtension
	{
		/*
		    GetCreateNamespace looks through the program for the namespace object matching the
		    given fullname.
		    If no namespace object is found, it is created and inserted into program.
		*/
		public static IRNamespace GetCreateNamespace(this IRProgram program, string nsFullName)
		{
			var targetNS = program.Namespaces.FirstOrDefault(ns => ns.FullName.Equals(nsFullName));
			if (targetNS == null)
			{
				targetNS = new IRNamespace(nsFullName, null);
				program.Namespaces.Add(targetNS);
			}

			return targetNS;
		}

		/*
		    MoveTypeToNamespace retrieves the type matching typeFullName and moves it from the original namespace
		    into the target namespace.

		    Returns true if the type was found and moved. False if the type was not found.
		*/
		public static bool MovePublicTypeToNamespace(this IRProgram program, string typeFullName,
													 IRNamespace targetNamespace)
		{
			bool found = false;
			IRNamespace sourceNS = null;
			IRClass foundClass = null;
			IREnum foundEnum = null;

			// Locate the type - could be class or enum.
			foreach (var ns in program.Namespaces)
			{
				foundClass = ns.Classes.FirstOrDefault(c => c.FullName.Equals(typeFullName));
				foundEnum = ns.Enums.FirstOrDefault(e => e.FullName.Equals(typeFullName));

				// If found, remove it from the parent namespace.
				if (foundClass != null || foundEnum != null)
				{
					// We don't move private types!
					if (foundClass?.IsPrivate == true || foundEnum?.IsPrivate == true)
					{
						throw new IRMoveException("This method does not move private types!");
					}

					ns.Classes.Remove(foundClass);
					ns.Enums.Remove(foundEnum);
					found = true;
					sourceNS = ns;
					break;
				}
			}

			// Add the type to the target namespace.
			if (found)
			{
				if (foundClass != null)
				{
					targetNamespace.Classes.Add(foundClass);
					foundClass.UpdateTypeReferences(targetNamespace, sourceNS);
					// And move all private types under the new namespace.
					foundClass.MovePrivateTypesToNamespace(targetNamespace, sourceNS);
				}
				else if (foundEnum != null)
				{
					targetNamespace.Enums.Add(foundEnum);
					foundEnum.UpdateTypeReferences(targetNamespace, sourceNS);
				}
			}

			return found;
		}

		/*
		    Moves all private types of the given class into the new namespace.
		*/
		public static bool MovePrivateTypesToNamespace(this IRClass irClass,
													   IRNamespace targetNamespace, IRNamespace sourceNamespace)
		{
			var types = irClass.PrivateTypes;
			var privateEnums = types.Where(t => t is IREnum).Cast<IREnum>().ToList();
			var privateClasses = types.Where(t => t is IRClass).Cast<IRClass>().ToList();

			// Remove all types from the old namespace.
			sourceNamespace.Classes.RemoveAll(c => privateClasses.Contains(c));
			sourceNamespace.Enums.RemoveAll(e => privateEnums.Contains(e));

			// Add all types into the new namespace.
			targetNamespace.Classes.AddRange(privateClasses);
			targetNamespace.Enums.AddRange(privateEnums);

			foreach (var movedType in types)
			{
				movedType.UpdateTypeReferences(targetNamespace, sourceNamespace);
			}

			// Private types can also be IRClasses, so recursively perform the same operation on each
			// private class.
			foreach (var privateClass in privateClasses)
			{
				privateClass.MovePrivateTypesToNamespace(targetNamespace, sourceNamespace);
			}

			return true;
		}

		/*
		    Updates all references made from the given type to other types.
		*/
		public static void UpdateTypeReferences(this IRTypeNode type, IRNamespace newNS,
												IRNamespace oldNS)
		{
			type.UpdateTypeName(newNS);
			type.UpdateParent(newNS);
		}

		/*
		    Updates the naming of the given type.
		*/
		public static void UpdateTypeName(this IRTypeNode type, IRNamespace newNS)
		{
			// Only update the full name.
			type.FullName = newNS.FullName.Trim('.') + "." + type.ShortName;
			// Don't touch the shortname!
		}

		/*
		    Updates the parent for non private types.
		*/
		public static void UpdateParent(this IRTypeNode type, IRNamespace newNS)
		{
			if (type.IsPrivate == false)
			{
				type.Parent = newNS;
			}
			else
			{
				// We leave private types alone because they must keep referencing their parent class.
			}
		}
	}

	public class IRMoveException : Exception
	{
		public IRMoveException(string message)
		: base(message)
		{
		}

		public IRMoveException(string message, Exception inner)
		: base(message, inner)
		{
		}
	}
}
