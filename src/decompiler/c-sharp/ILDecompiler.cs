using Mono.Cecil;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.decompiler.c_sharp
{
    class ILDecompiler : DefaultDecompiler<TypeDefinition>
    {
        /*
         * Class that decompiles information about a specific type.
         * This could be a Class or an Enum. 
         */

        // The actual IR result that was constructed.
        IR.IRTypeNode _constructedSubject;

        // Returns TRUE is the target definition is an enum.
        public bool IsEnum
        {
            get
            {
                return _subject.IsEnum;
            }
        }

        // The target is suspected to be validated by MatchAnalyzableClasses(..)
        public ILDecompiler(TypeDefinition subject) : base(subject) { }

        // Use this method to match all definitions that can be processed by this
        // class. Eg: All classes that have the interface IProtoBuf!
        public static bool MatchAnalyzableClasses(TypeDefinition t)
        {
            return (t.IsClass && t.Interfaces.Any(i => i.Name.Equals("IProtoBuf")));
        }

        public override IRClass ConstructIRClass()
        {
            return (IR.IRClass)_constructedSubject;
        }

        public override IR.IREnum ConstructIREnum()
        {
            return (IR.IREnum)_constructedSubject;
        }

        public override void Decompile(out List<TypeDefinition> references)
        {
            // Create a new IRType to be filled in.
            if (IsEnum)
            {
                _constructedSubject = new IREnum(_subject.FullName, _subject.Name)
                {
                    Properties = new List<IREnumProperty>(),
                };
                // Extract the properties from this enum, local function.
                ExtractEnumProperties();

                // Enums have no references to other types.
                references = new List<TypeDefinition>();
            }
            else
            {
                var irClass = new IRClass(_subject.FullName, _subject.Name)
                {
                    PrivateTypes = new List<IRTypeNode>(),
                    Properties = new List<IRClassProperty>(),
                };
                _constructedSubject = irClass;

                // Fetch the properties of the type...
                // (At the same time, all references of this class are being collected.)
                var props = SilentOrbitInspector.ExtractClassProperties(_subject, out references);
                // and store the properties.
                irClass.Properties = props;

                // Extract necessary methods for decompilation
                var serializeEnum = _subject.Methods.Where(SilentOrbitInspector.MatchSerializeMethod);
                var deserializeEnum = _subject.Methods.Where(SilentOrbitInspector.MatchDeserializeMethod);
                if (!serializeEnum.Any() || !deserializeEnum.Any())
                {
                    throw new ExtractionException("No serialize or deserialize methods found!");
                }
                MethodDefinition serialize = serializeEnum.First();
                MethodDefinition deserialize = deserializeEnum.First();

                // Create a handler for the OnCall action.
                Action<CallInfo, List<byte>> silentOrbitSerializeCallHandler = (CallInfo info, List<byte> w) =>
                {
                    // Just chain the call.
                    // Property information is updated in place!
                    SilentOrbitInspector.SerializeOnCall(info, w, props);
                };

                // Walk the serialize method for additional information.
                MethodWalker.WalkMethod(serialize, silentOrbitSerializeCallHandler, null);
                // Walk the deserialize method for additional information.
            }
        }

        // Construct IR property objects from enum fields
        private void ExtractEnumProperties()
        {
            // Get the underlying type of the enum
            var enumUnderLyingType = PropertyTypeKind.UNKNOWN;
            var enumMagicVal = _subject.Fields.First(x => x.Name.Equals("value__"));
            if (enumMagicVal.FieldType.FullName.EndsWith("Int32"))
            {
                enumUnderLyingType = PropertyTypeKind.INT32;
            }
            else if (enumMagicVal.FieldType.FullName.EndsWith("UInt32"))
            {
                enumUnderLyingType = PropertyTypeKind.UINT32;
            }
            else
            {
                // WARN: The underlying type is unknown!
                throw new Exception("Underlying enum type is unsupported!");
            }

            List<IREnumProperty> props = new List<IREnumProperty>();
            foreach (var field in _subject.Fields)
            {
                if (field.Name.Equals("value__"))
                {
                    // This field holds the underlying value type. See above.
                    continue;
                }

                // Convert the constant value to int
                int? enumValue = null;
                if (enumUnderLyingType == PropertyTypeKind.INT32)
                {
                    enumValue = (int)field.Constant;
                }
                else if (enumUnderLyingType == PropertyTypeKind.UINT32)
                {
                    enumValue = (int)(uint)field.Constant;
                }

                // Add a new property to the list for this enum field
                props.Add(new IREnumProperty
                {
                    // Straight name copy
                    Name = field.Name,
                    // If the enumValue is NOT NULL, use enum value.. else use the integer 0
                    Value = (enumValue ?? 0)
                });
            }

            // Save properties
            ((IREnum)_constructedSubject).Properties = props;
        }
    }
}
