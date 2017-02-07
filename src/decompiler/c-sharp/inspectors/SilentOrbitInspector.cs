using Mono.Cecil;
using Mono.Cecil.Cil;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace protoextractor.decompiler.c_sharp.inspectors
{
    class SilentOrbitInspector
    {

        public static bool MatchAnalyzableClasses(TypeDefinition t)
        {
            return (t.IsClass && t.Interfaces.Any(i => i.Name.Equals("IProtoBuf")));
        }

        // Math the SilentOrbit generated Deserialize method.
        public static bool MatchDeserializeMethod(MethodDefinition method)
        {
            return method.Name.Equals("Deserialize") && method.Parameters.Count == 3;
        }
        // Match the SilentOrbit generated Serialize method.
        public static bool MatchSerializeMethod(MethodDefinition method)
        {
            return method.Name.Equals("Serialize") && method.Parameters.Count == 2;
        }

        public static void DeserializeOnCall(CallInfo info, List<byte> writtenBytes, List<IRClassProperty> properties)
        {
            // Check if we matched a function call that sets properties, without reading from the wire.
            if (info.Conditions.Count == 0 && info.Method.Name.StartsWith("set_"))
            {
                // Extract property name.
                var propName = info.Method.Name.Substring(4);
                // Find property.
                var property = properties.First(x => x.Name.Equals(propName));

                // Hardcode internationalization values for converting values to strings.
                var prevInternationalization = Thread.CurrentThread.CurrentCulture;               
                Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");
                // Get the value that's gonna be set to the property.
                var val = info.Arguments[1].ToString();
                // Restore.
                Thread.CurrentThread.CurrentCulture = prevInternationalization;

                // Generate correct string representation for each type of object
                // that could be set.
                if (val.EndsWith("String::Empty"))
                {
                    val = "\"\"";
                }
                else if (info.Arguments[1].GetType() == typeof(string))
                {
                    val = "\"" + val.Replace("\"", "\\\"") + "\"";
                }
                if (info.Method.Parameters.First().ParameterType.Name == "Boolean")
                {
                    val = (val == "0") ? "false" : "true";
                }
                // Set the default value.
                property.Options.DefaultValue = val;
            }
        }

        // A method was called by our inspected method. We use the collected information (our environment)
        // to extract information about the type (and fields).
        public static void SerializeOnCall(CallInfo info, List<byte> writtenBytes, List<IRClassProperty> properties)
        {
            if (info.Method.Name == "WriteByte")
            {
                int byteAmount = (int)info.Arguments[1];
                writtenBytes.Add((byte)byteAmount);
                return;
            }
            if (info.Arguments.Any(x => x.ToString().Contains("GetSerializedSize()")))
            {
                return;
            }
            if (!info.Method.Name.StartsWith("Write") && info.Method.Name != "Serialize")
            {
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
            if (listConds.Any() && !iterConds.Any(x => x.Cmp == Comparison.IsTrue))
            {
                // Skip packed size writes:
                return;
            }

            // Get the packed flag.
            var packed = IsFieldPacked(iterConds);

            // Get the tag from the info.
            var tag = InspectorTools.GetFieldTag(writtenBytes);
            // Reset written bytes in order to process the next tag.
            writtenBytes.Clear();

            // Get the field label.
            var label = GetFieldLabel(info, iterConds);

            // Get the name for the current tag.
            var name = GetFieldName(info, label);

            // Fetch the property object by it's name
            var prop = properties.First(x => x.Name == name);

            // In case the writing class is called BinaryWriter, the mapping is different.
            if(info.Method.DeclaringType.Name.Equals("BinaryWriter"))
            {
                prop.Type = FixedTypeMapper(prop);
            }

            // Set property options
            prop.Options.Label = label;
            prop.Options.PropertyOrder = tag;
            prop.Options.IsPacked = packed;
            // Default value is extracted from the deserialize method..
        }

        // Get all properties from the type we are analyzing.
        public static List<IRClassProperty> ExtractClassProperties(TypeDefinition _subjectClass, out List<TypeDefinition> references)
        {
            // Will contain all typedefinitions of types referenced by this class.
            references = new List<TypeDefinition>();

            // All properties for the given class definition.
            List<IRClassProperty> properties = new List<IRClassProperty>();
            // Property != field
            // Properties expose fields by providing a getter/setter.
            // The properties are public accessible data, and these are the things that map
            // to protobuffer files.
            foreach (var property in _subjectClass.Properties)
            {
                // Property must have a setter method, otherwise it wouldn't be related to ProtoBuffers schema.
                if (property.SetMethod == null) continue;

                // Object which the current property references.
                TypeDefinition refDefinition;
                // Set of field (proto) options.
                IRClassProperty.ILPropertyOptions options = new IRClassProperty.ILPropertyOptions();
                // Default to invalid field.
                options.Label = FieldLabel.INVALID;
                // IR type of the property.
                PropertyTypeKind propType = InspectorTools.DefaultTypeMapper(property, out refDefinition);

                // IR object - reference placeholder for the IR Class.
                IRTypeNode irReference = null;
                if (propType == PropertyTypeKind.TYPE_REF)
                {
                    irReference = InspectorTools.ConstructIRType(refDefinition);
                    // Also save the reference typedefinition for the caller to process.
                    references.Add(refDefinition);
                }

                // Construct IR property and store.
                var prop = new IRClassProperty
                {
                    Name = property.Name,
                    Type = propType,
                    ReferencedType = irReference,
                    Options = options,
                };
                properties.Add(prop);
            }

            return properties;
        }

        

        public static bool IsFieldPacked(IEnumerable<Condition> iterConds)
        {
            var packed = iterConds.Any(x => x.Cmp == Comparison.IsFalse);

            return packed;
        }

        public static FieldLabel GetFieldLabel(CallInfo info, IEnumerable<Condition> iterConds)
        {
            // Discover the field expectancy.
            // Fallback to REQUIRED AS DEFAULT.
            var label = FieldLabel.REQUIRED;
            if (iterConds.Any())
            {
                label = FieldLabel.REPEATED;
            }
            // There is a test for the specific field, so it's optional.
            else if (info.Conditions.Any(x => x.Lhs.Contains(".Has")))
            {
                label = FieldLabel.OPTIONAL;
            }
            
            return label;
        }

        public static string GetFieldName(CallInfo info, FieldLabel label)
        {
            // Extract the name of the field.
            var name = "";
            if (label == FieldLabel.REPEATED)
            {
                name = info.Conditions.First(x => x.Lhs.Contains("get_Count()")).Lhs;
                name = name.Substring(name.IndexOf(".get_") + 5);
                name = name.Substring(0, name.Length - 14);
            }
            else
            {
                name = info.Arguments[1].ToString();
                if (name.StartsWith("Encoding.get_UTF8()"))
                {
                    name = name.Substring(31, name.Length - 32);
                }
                name = name.Substring(name.IndexOf(".get_") + 5);
                name = name.Substring(0, name.Length - 2);
            }

            return name;
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
    }
}
