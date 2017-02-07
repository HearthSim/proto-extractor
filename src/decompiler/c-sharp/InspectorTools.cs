using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using protoextractor.IR;

namespace protoextractor.decompiler.c_sharp
{
    public class InspectorTools
    {
        // Converts a given typedefinition to an empty IR type.
        // This method can be used to generate reference placeholders for properties.
        public static IRTypeNode ConstructIRType(TypeDefinition type)
        {
            if (type.IsEnum)
            {
                return new IREnum(type.FullName, type.Name);
            }
            else if (type.IsClass)
            {
                return new IRClass(type.FullName, type.Name);
            }
            else
            {
                throw new Exception("The given type can not be represented by IR");
            }
        }

        public static PropertyTypeKind DefaultTypeMapper(PropertyDefinition property, out TypeDefinition referencedType)
        {
            PropertyTypeKind fieldType = PropertyTypeKind.UNKNOWN;
            // If the property is a reference to another type, this variable contains the typedefinition. 
            // Defaults to null.
            referencedType = null;

            // The actual type object of the property.
            TypeReference type = property.PropertyType;
            // The type could be a list<X> (GENERIC), so the actual type would be the first template
            // parameter.
            if (type.IsGenericInstance)
            {
                // We SUPPOSE the actual type is a List with 1 generic parameter.
                type = (type as GenericInstanceType).GenericArguments.First();
            }
            // Get the type by literal name.
            fieldType = LiteralTypeMapper(type.Name);
            if (fieldType == PropertyTypeKind.TYPE_REF)
            {
                // The type is not a primitive and references something else.
                // We just resolve the reference and pass it back.. the caller can
                // decide what to do with it.
                var typeDefinition = type.Resolve();
                referencedType = typeDefinition;
            }

            return fieldType;
        }

        public static PropertyTypeKind LiteralTypeMapper(string type)
        {
            PropertyTypeKind fieldType;
            switch (type)
            {
                case "Int32":
                    fieldType = PropertyTypeKind.INT32;
                    break;
                case "Int64":
                    fieldType = PropertyTypeKind.INT64;
                    break;
                case "UInt32":
                    fieldType = PropertyTypeKind.UINT32;
                    break;
                case "UInt64":
                    fieldType = PropertyTypeKind.UINT64;
                    break;
                case "SInt32":
                    fieldType = PropertyTypeKind.SINT32;
                    break;
                case "SInt64":
                    fieldType = PropertyTypeKind.SINT64;
                    break;
                case "Fixed32":
                    fieldType = PropertyTypeKind.FIXED32;
                    break;
                case "Fixed64":
                    fieldType = PropertyTypeKind.FIXED64;
                    break;
                case "SFixed32":
                    fieldType = PropertyTypeKind.SFIXED32;
                    break;
                case "SFixed64":
                    fieldType = PropertyTypeKind.SFIXED64;
                    break;
                case "Boolean":
                case "Bool":
                    fieldType = PropertyTypeKind.BOOL;
                    break;
                case "String":
                    fieldType = PropertyTypeKind.STRING;
                    break;
                case "Byte[]": // Silentorbit
                case "ByteString": // Google
                case "Bytes":
                    fieldType = PropertyTypeKind.BYTES;
                    break;
                //////////////////////////////////
                case "Double":
                    fieldType = PropertyTypeKind.DOUBLE;
                    break;
                case "Float":
                case "Single":
                    fieldType = PropertyTypeKind.FLOAT;
                    break;
                case "Enum":
                default:
                    // Suppose the type is a reference.
                    fieldType = PropertyTypeKind.TYPE_REF;
                    break;
            }

            return fieldType;
        }

        public static int GetFieldTag(List<byte> written)
        {
            // Extract the field index from the bytes prepended to the actual field data.
            // See protobuffer encoding for more info about varints and such..
            var tag = 0;
            var i = 0;
            while (true)
            {
                var b = written[i];
                tag |= (b & 0x7f) << (7 * i);
                i += 1;
                if (0 == (b & 0x80)) break;
            }
            if (i != written.Count)
            {
                throw new InvalidProgramException(
                    "bad tag bytes, not gonna recover from this state");
            }
            // Remove all tracked bytes.
            // The next field tag can be tracked.
            written.Clear();
            tag >>= 3;

            return tag;
        }
    }
}
