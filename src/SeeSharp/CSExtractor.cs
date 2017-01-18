using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using protoextractor.extractor;

namespace protoextractor.SeeSharp
{
    class CSExtractor : Extractor<TypeDefinition>
    {

        private AssemblyDefinition _targetAssembly;

        /* 
         * Caches for objects. These caches make the resolve time for second lookups instant.
         * They also make sure the Garbage Collector won't remove the resolved objects.
         */

        private TypeDefinition[] _analyzableTypesCache;
        private Dictionary<TypeDefinition, ILClassType> _classCache = new Dictionary<TypeDefinition, ILClassType>();
        private Dictionary<TypeDefinition, ILEnumType> _enumCache = new Dictionary<TypeDefinition, ILEnumType>();

        public CSExtractor()
        {
        }

        public void SetTargetAssembly(AssemblyDefinition assembly)
        {
            if (_targetAssembly != assembly)
            {
                // Relink the assembly
                _targetAssembly = assembly;
                // Clear all caches for previous assembly
                ClearCache();
            }
        }

        private void ClearCache()
        {
            // Arrays
            _analyzableTypesCache = null;
            _analyzableTypesCache = null;

            // Dictionaries
            _classCache.Clear();
            _enumCache.Clear();
        }

        /* SELECTORS */

        private bool MatchAnalyzableTypes(TypeDefinition type)
        {
            // Select all types which have an interface that's called IProtoBuf
            return type.Interfaces.Any(i => i.Name.Equals("IProtoBuf")) ||
            // Or return if the type is an enum
                    type.IsEnum;
        }

        private bool MatchDeserializeMethod(MethodDefinition method)
        {
            return method.Name.Equals("Deserialize") && method.Parameters.Count == 3;
        }

        private bool MatchSerializeMethod(MethodDefinition method)
        {
            return method.Name.Equals("Serialize") && method.Parameters.Count == 2;
        }


        // Extracts a type from the set assembly. The type will be saved and returned.
        public ILType ExtractType(TypeDefinition type)
        {
            ILType returnValue = null;
            if (type.IsEnum)
            {
                returnValue = ExtractEnum(type);
            }
            else if (type.IsClass)
            {
                returnValue = ExtractClass(type);
            }

            return returnValue;
        }

        // Delegated from ExtractType
        private ILEnumType ExtractEnum(TypeDefinition type)
        {
            ILEnumType theEnum;
            _enumCache.TryGetValue(type, out theEnum);
            if (theEnum == null)
            {
                // Parse enum fields into properties
                ILEnumProperty[] properties = EnumToProperties(type);
                // Construct new enum
                theEnum = new ILEnumType
                {
                    name = type.Name,
                    properties = properties
                };
                // Save the enum into the cache
                _enumCache.Add(type, theEnum);
            }
            return theEnum;
        }

        // Construct IL property objects from enum fields
        private ILEnumProperty[] EnumToProperties(TypeDefinition type)
        {
            List<ILEnumProperty> props = new List<ILEnumProperty>();
            foreach (var field in type.Fields)
            {
                if (field.Name.Equals("value__"))
                {
                    // This field holds the underlying value type.
                    // It should be integer
                    continue;
                }
                // Convert the constant value to int
                int? enumValue = (int)field.Constant;
                // Add a new property to the list for this enum field
                props.Add(new ILEnumProperty
                {
                    // Straight name copy
                    name = field.Name,
                    // If the enumValue is NOT NULL, use enum value.. else use the integer 0
                    value = (enumValue ?? 0)
                });
            }

            return props.ToArray();
        }

        // Delegated from ExtractType
        private ILClassType ExtractClass(TypeDefinition type)
        {
            // Extract data from the class
            ILClassType returnValue;
            _classCache.TryGetValue(type, out returnValue);

            if (returnValue == null)
            {
                returnValue = ExtractExplicitClass(type);
                // Extract private enums!
                foreach (var subtype in type.NestedTypes)
                {
                    if (subtype.IsEnum)
                    {
                        ExtractEnum(subtype);
                    }
                }
                // Store in cache for future reference
                _classCache.Add(type, returnValue);
            }
            return returnValue;
        }

        private ILClassType ExtractExplicitClass(TypeDefinition type)
        {
            // Keep track of default values
            var defaults = new Dictionary<string, string>();
            // TODO
            List<byte> written = new List<byte>();

            // Target the deserialize method for TODO
            MethodDefinition DeserializeTarget = type.Methods.First(MatchDeserializeMethod);
            MethodWalker Dwalker = new MethodWalker(DeserializeTarget);
            // Register ourselves for post processing of method
            Dwalker.OnCall = info => { ExtractDeserializationData(info, defaults); };
            Dwalker.Walk();

            // Target the serialize method for TODO
            MethodDefinition SerializeTarget = null;
            MethodWalker Swalker = new MethodWalker(SerializeTarget);
            // ExtractSerializationData(MethodWalker.CallInfo info, TypeDefinition type, List<byte> written,
            // Dictionary<string, string> defaults, object result)
            Swalker.OnCall = info => { ExtractSerializationData(info, type, written, defaults, null); };
            Swalker.Walk();


            return null;
        }

        private void ExtractDeserializationData(CallInfo info, Dictionary<string, string> defaults)
        {
            // TODO Cleanup
            if (info.Conditions.Count == 0 && info.Method.Name.StartsWith("set_"))
            {
                var fieldName = info.Method.Name.Substring(4).ToLowerUnder();
                var val = info.Arguments[1].ToString();
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
                    val = val == "0" ? "false" : "true";
                }
                defaults[fieldName] = val;
            }
        }

        // Legacy code
        HashSet<TypeDefinition> enumTypes = new HashSet<TypeDefinition>();

        // Result == OLD MessageNode structure
        private void ExtractSerializationData(CallInfo info, TypeDefinition type, List<byte> written,
            Dictionary<string, string> defaults, object result)
        {
            // Every field written to the output stream is prepended by a byte value.
            // That byte value contains the encoding used and the index of the field.
            if (info.Method.Name == "WriteByte")
            {
                written.Add((byte)(int)info.Arguments[1]);
                return;
            }
            // GetSerializedSize is used to write another protobuffer format to the stream.
            // The next field written to the stream has a referenced type to another class.
            if (info.Arguments.Any(x => x.ToString().Contains("GetSerializedSize()")))
            {
                return;
            }
            // Don't process method calls that do not put data on the stream.
            // This can be done because we hooked the serialize method specifically.
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
            var packed = iterConds.Any(x => x.Cmp == Comparison.IsFalse);

            // Discover the field expectancy.
            // Invalid is the default.
            var label = FieldLabel.INVALID;
            if (iterConds.Any())
            {
                label = FieldLabel.REPEATED;
            }
            // There is a test for the specific field, so it's optional.
            else if (info.Conditions.Any(x => x.Lhs.Contains(".Has")))
            {
                label = FieldLabel.OPTIONAL;
            }
            else
            {
                label = FieldLabel.REQUIRED;
            }

            // Get name:
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
            var prop = type.Properties.First(x => x.Name == name);
            name = name.ToLowerUnder();

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

            // Parse field type
            // Protobuf type
            // var fieldType = PropertyTypeKind.UNKNOWN;
            // Real field type
            var subType = PropertyTypeKind.UNKNOWN;

            // The type of the field is an enum
            if (prop.PropertyType.Resolve().IsEnum)
            {
                // This type is an enum..
                fieldType = FieldType.Enum;
                var enumType = prop.PropertyType;
                enumTypes.Add(enumType.Resolve());
                subType = enumType.PackageName();
                fieldType = FieldType.Enum;
                if (defaults.ContainsKey(name))
                {
                    var intVal = Int32.Parse(defaults[name]);
                    defaults[name] = enumType.Resolve().Fields
                        .First(x => x.HasConstant && intVal == (int)x.Constant)
                        .Name;
                }
            }
            else if (info.Method.Name == "Serialize")
            {
                var messageType = info.Method.DeclaringType;
                subType = messageType.PackageName();
                fieldType = FieldType.Message;
            }
            else if (info.Method.DeclaringType.Name == "ProtocolParser")
            {
                var innerType = prop.PropertyType;
                if (innerType.IsGenericInstance)
                {
                    innerType = (innerType as GenericInstanceType).GenericArguments.First();
                }
                switch (innerType.Name)
                {
                    // Int32, Int64,
                    // UInt32, UInt64,
                    // Bool, String, Bytes
                    case "Int32":
                        fieldType = FieldType.Int32;
                        break;
                    case "Int64":
                        fieldType = FieldType.Int64;
                        break;
                    case "UInt32":
                        fieldType = FieldType.UInt32;
                        break;
                    case "UInt64":
                        fieldType = FieldType.UInt64;
                        break;
                    case "Boolean":
                        fieldType = FieldType.Bool;
                        break;
                    case "String":
                        fieldType = FieldType.String;
                        break;
                    case "Byte[]":
                        fieldType = FieldType.Bytes;
                        break;
                    default:
                        //Console.WriteLine("unresolved type for field '" + name + "' in " + result.Name.Text);
                        break;
                }
            }
            else if (info.Method.DeclaringType.Name == "BinaryWriter")
            {
                // Double, Float,
                // Fixed32, Fixed64,
                // SFixed32, SFixed64,
                switch (info.Method.Parameters.First().ParameterType.Name)
                {
                    case "Double":
                        fieldType = FieldType.Double;
                        break;
                    case "Single":
                        fieldType = FieldType.Float;
                        break;
                    case "UInt32":
                        fieldType = FieldType.Fixed32;
                        break;
                    case "UInt64":
                        fieldType = FieldType.Fixed64;
                        break;
                    default:
                        Console.WriteLine("unresolved type");
                        break;
                }
            }
            if (fieldType == FieldType.Invalid)
            {
                //Console.WriteLine("unresolved type for field '" + name + "' in " + result.Name.Text);
            }

            var field = new FieldNode(name, label, fieldType, tag);
            field.TypeName = subType;
            field.Packed = packed;
            if (defaults.ContainsKey(name))
            {
                field.DefaultValue = defaults[name];
            }
            // result.Fields.Add(field);
        }

        public TypeDefinition[] GetAnalyzableTypes()
        {
            if (_analyzableTypesCache == null)
            {
                // TODO; parse service descriptions
                var allTypes = _targetAssembly.MainModule.GetTypes();
                _analyzableTypesCache = allTypes.Where(MatchAnalyzableTypes).ToArray();
            }

            // Return a copy of the array in cache
            return _analyzableTypesCache.ToArray();
        }

        public Namespace GetNamespace(TypeDefinition type)
        {
            throw new NotImplementedException();
        }

        public Namespace[] GetResolvedNamespaces()
        {
            throw new NotImplementedException();
        }

        public void ResolveProperties(TypeDefinition type)
        {
            throw new NotImplementedException();
        }
    }
}
