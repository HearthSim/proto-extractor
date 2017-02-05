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
            switch (type.Name)
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
                case "Boolean":
                    fieldType = PropertyTypeKind.BOOL;
                    break;
                case "String":
                    fieldType = PropertyTypeKind.STRING;
                    break;
                case "Byte[]":
                    fieldType = PropertyTypeKind.BYTES;
                    break;
                //////////////////////////////////
                case "Double":
                    fieldType = PropertyTypeKind.DOUBLE;
                    break;
                case "Single":
                    fieldType = PropertyTypeKind.FLOAT;
                    break;
                default:
                    // The type is not a primitive and references something else.
                    // We just resolve the reference and pass it back.. the caller can
                    // decide what to do with it.
                    var typeDefinition = type.Resolve();
                    referencedType = typeDefinition;
                    // Register the property to be referencing something else.
                    fieldType = PropertyTypeKind.TYPE_REF;
                    break;
            }

            return fieldType;
        }

        public static PropertyTypeKind FixedTypeMapper(IRClassProperty property)
        {
            PropertyTypeKind fieldType;
            switch (property.Type)
            {
                case PropertyTypeKind.UINT32:
                    fieldType = PropertyTypeKind.FIXED32;
                    break;
                case PropertyTypeKind.UINT64:
                    fieldType = PropertyTypeKind.FIXED64;
                    break;
                default:
                    fieldType = property.Type;
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
