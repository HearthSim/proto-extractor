using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.extractor
{
    // List of types that build our generic programming language //

    // The generic program. It consists of all namespaces that are resolved by the extractor.
    public class Program
    {
        public Namespace[] namespaces;
        public Service[] services;
    }

    public class Service
    {
        // TODO; Flesh this out
    }

    // Maps directly to every programming language. All namespaces are flattened, there is no relation
    // between namespace 'a' and namespace 'a.b'. 'a.b' has a separate Namespace object.
    public class Namespace
    {
        public string name;
        public ILClassType[] types;
        public ILEnumType[] enums;
    }

    // Represents any type
    abstract public class ILType { }

    public class ILClassType : ILType
    {
        public string name;
        public ILClassProperty[] properties;
    }

    public class ILEnumType : ILType
    {
        public string name;
        public ILEnumProperty[] properties;
    }

    // Represents any property
    abstract public class ILProperty { }

    public class ILClassProperty : ILProperty
    {
        public string name;
        public int fieldOrder;
        public ILFieldOptions fieldOptions;

        public PropertyTypeKind type;
        public ILPropertyValue value;
    }

    public class ILFieldOptions
    {
        FieldLabel label;

    }

    public class ILPropertyValue
    {
        public bool defaultValue; // object?
        public ILType typeReference;
    }

    public class ILEnumProperty : ILProperty
    {
        public string name;
        public int value;
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
