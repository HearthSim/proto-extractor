using Mono.Cecil;
using Mono.Cecil.Cil;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace protoextractor.decompiler.c_sharp
{
    class SilentOrbitInspector
    {

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

            // Get the name for the current tag.
            var name = "";
            if (iterConds.Any())
            {
                // Iteration methods are found, so this field is repeated.
                name = info.Conditions.First(x => x.Lhs.Contains("get_Count()")).Lhs;
                name = name.Substring(name.IndexOf(".get_") + 5);
                name = name.Substring(0, name.Length - 14);
            }
            else
            {
                // NON-REPEATED field
                name = info.Arguments[1].ToString();
                if (name.StartsWith("Encoding.get_UTF8()"))
                {
                    name = name.Substring(31, name.Length - 32);
                }
                name = name.Substring(name.IndexOf(".get_") + 5);
                name = name.Substring(0, name.Length - 2);
            }
            // Fetch the property object by it's name
            var prop = properties.First(x => x.Name == name);

            // Get the packed flag.
            var packed = InspectorTools.IsFieldPacked(iterConds);

            // Get the tag from the info.
            var tag = InspectorTools.GetFieldTag(writtenBytes);
            // Reset written bytes in order to process the next tag.
            writtenBytes.Clear();

            // Set property options
            prop.Options.PropertyOrder = tag;
            prop.Options.IsPacked = packed;
            // Label and value should have been set!
            // Default value is extracted from the deserialize method..
            // TODO ^^^
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
                // Property must have a setter method, otherwise it wouldn't be related to ProtoBuffers
                if (property.SetMethod == null) continue;

                // Object which the current property references.
                TypeDefinition refDefinition;
                // Set of field (proto) options.
                IRClassProperty.ILPropertyOptions options = new IRClassProperty.ILPropertyOptions();
                // IR type of the property.
                PropertyTypeKind propType = InspectorTools.TypeMapper(property, options, out refDefinition);

                // IR object - reference placeholder for the IR Class.
                IRTypeNode irReference = null;
                if (propType == PropertyTypeKind.TYPE_REF)
                {
                    irReference = ConstructIRType(refDefinition);
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
    }
}
