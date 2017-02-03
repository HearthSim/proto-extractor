using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.IR
{
    /*
     * Namespace for intermediate programming language structures/types.
     * Like any modern compiler, the project is split in a front- and backend.
     * 
     * The frontend handles the parsing of incoming data, while producing a universal
     * intermediate structure of that data: the Intermediate Representation.
     * 
     * The backend takes this intermediate representation and produces the desired 
     * output.
     */
    [DebuggerDisplay("IRProgram")]
    public class IRProgram
    {
        /*
         * Represents a complete program.
         */

        public List<IRNamespace> Namespaces;
    }

    public class IRProgramNode
    {
        /*
         * Every IR object should be usable in a flexible way.
         * This class guarantees certain properties are always set.
         */

        public string FullName;
        public string ShortName;
        // Indicates if this IR is a nested (private) type. Defaults to false.
        public bool IsPrivate;
        // The IR parent object for this one.
        // Only namespaces are expected to NOT have a parent reference set.
        public IRProgramNode Parent;

        // Constructor forces these properties to be set at instance creation.
        public IRProgramNode(string _fullName, string _shortName)
        {
            FullName = _fullName;
            ShortName = _shortName;
            IsPrivate = false;
            Parent = null;
        }

    }

    [DebuggerDisplay("IRNamespace {FullName}")]
    public class IRNamespace : IRProgramNode
    {
        // All classes found directly under this namespace.
        public List<IRClass> Classes;
        // All enums found directly inder this namespace.
        public List<IREnum> Enums;

        public IRNamespace(string _fullName, string _shortName) : base(_fullName, _shortName) { }
    }

    public class IRTypeNode : IRProgramNode
    {
        public IRTypeNode(string _fullName, string _shortName) : base(_fullName, _shortName) { }
    }

    [DebuggerDisplay("IRClass {FullName}")]
    public class IRClass : IRTypeNode
    {
        // All properties of this class.
        public List<IRClassProperty> Properties;
        // All types that are only referenceble by properties of this
        // specific class.
        public List<IRTypeNode> PrivateTypes;

        public IRClass(string _fullName, string _shortName) : base(_fullName, _shortName) { }
    }

    [DebuggerDisplay("IREnum {FullName}")]
    public class IREnum : IRTypeNode
    {
        // All properties of this enum.
        public List<IREnumProperty> Properties;

        public IREnum(string _fullName, string _shortName) : base(_fullName, _shortName) { }
    }

    [DebuggerDisplay("IRProperty {Type} -- {Name}")]
    public class IRClassProperty
    {

        public class ILPropertyOptions
        {
            /*
             * We must have some explicit details about properties regarding
             * protobuffer, because this data cannot be guessed if not extracted
             * directly from the sources.
             * See protobuffer manual for more information about the options.
             */

            public FieldLabel Label;
            public int PropertyOrder;
            public bool IsPacked;
            public object DefaultValue;
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


    public enum PropertyTypeKind
    {
        UNKNOWN = 0,
        DOUBLE,
        FLOAT,

        INT32,
        INT64,
        UINT32,
        UINT64,
        //SINT32,
        //SINT64,

        FIXED32,
        FIXED64,
        //SFIXED32,
        //SFIXED64,

        BOOL,
        STRING,
        BYTES,
        // Reference to other 
        TYPE_REF
    }

    public enum FieldLabel
    {
        INVALID = 0,
        OPTIONAL,
        REQUIRED,
        REPEATED
    }
}
