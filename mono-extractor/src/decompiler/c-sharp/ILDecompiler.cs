using Mono.Cecil;
using protoextractor.compiler.proto_scheme;
using protoextractor.decompiler.c_sharp.inspectors;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace protoextractor.decompiler.c_sharp
{
	class ILDecompiler : DefaultDecompiler<TypeDefinition>
	{
		/*
		    Class that decompiles information about a specific type.
		    This could be a Class or an Enum.
		*/

		// The actual IR result that was constructed.
		private IR.IRTypeNode _constructedSubject;

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
		public static bool MatchDecompilableClasses(TypeDefinition t)
		{
			return
				// Validate SilentOrbit.
				SilentOrbitInspector.MatchDecompilableClasses(t) ||
				// Validate GoogleProtobuffer.
				GoogleCSInspector.MatchDecompilableClasses(t) ||
				// Validates GoogleProtobuffer V1.
				GoogleV1Inspector.MatchDecompilableClasses(t)
				;
		}

		public override IRClass ConstructIRClass()
		{
			return (IRClass)_constructedSubject;
		}

		public override IREnum ConstructIREnum()
		{
			return (IREnum)_constructedSubject;
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

				// Test for SilentOrbit decompilation.
				if (SilentOrbitInspector.MatchDecompilableClasses(_subject))
				{
					DecompileClass_SilentOrbit(irClass, out references);
				}
				// Test for Google Protobuffer V1 decompilation.
				else if (GoogleV1Inspector.MatchDecompilableClasses(_subject))
				{
					DecompileClass_GoogleV1(irClass, out references);
				}
				// Test for Google Protobuffer decompilation.
				else if (GoogleCSInspector.MatchDecompilableClasses(_subject))
				{
					DecompileClass_Google(irClass, out references);
				}
				// Else fail..
				else
				{
					throw new ExtractionException("Unrecognized proto compiler!");
				}
			}
		}

		private void DecompileClass_SilentOrbit(IRClass target, out List<TypeDefinition> references)
		{
			// Fetch the properties of the type...
			// (At the same time, all references of this class are being collected.)
			var props = SilentOrbitInspector.ExtractClassProperties(_subject, out references);
			// and store the properties.
			target.Properties = props;

			// Extract necessary methods for decompilation
			var serializeEnum = _subject.Methods.Where(SilentOrbitInspector.MatchSerializeMethod);
			var deserializeEnum = _subject.Methods.Where(SilentOrbitInspector.MatchDeserializeMethod);
			if (!serializeEnum.Any() || !deserializeEnum.Any())
			{
				throw new ExtractionException("No serialize or deserialize methods found!");
			}
			MethodDefinition serialize = serializeEnum.First();
			MethodDefinition deserialize = deserializeEnum.First();

			// Create a handler for the serialize OnCall action.
			Action<CallInfo, List<byte>> silentOrbitSerializeCallHandler = (CallInfo info,
					List<byte> w) =>
			{
				// Just chain the call.
				// Property information is updated in place!
				SilentOrbitInspector.SerializeOnCall(info, w, props);
			};
			// Walk the serialize method for additional information.
			MethodWalker.WalkMethod(serialize, silentOrbitSerializeCallHandler, null);

			// Create handler for deserialize oncall action.
			Action<CallInfo, List<byte>> silentOrbitDeserializeCallHandler = (CallInfo info,
					List<byte> w) =>
			{
				// Just chain the call.
				// Property information is updated in place!
				SilentOrbitInspector.DeserializeOnCall(info, w, props);
			};
			// Walk the deserialize method for additional information.
			MethodWalker.WalkMethod(deserialize, silentOrbitDeserializeCallHandler, null);
		}

		private void DecompileClass_GoogleV1(IRClass target, out List<TypeDefinition> references)
		{
			// Setup containers
			var fieldNames = new List<string>();
			var targetProperties = new List<IRClassProperty>();
			var allFields = GoogleV1Inspector.ExtractClassFields(_subject);
			// .. and store the resulting properties into the target class.
			target.Properties = targetProperties;

			// Extract direct fields from the static data of the class.
			var staticConstructor = _subject.Methods.First(GoogleV1Inspector.MatchStaticConstructor);
			Action<CallInfo, List<byte>> cctorOnCall = (CallInfo c, List<byte> w) =>
			{
				GoogleV1Inspector.StaticCctorOnCall(c);
			};
			Action<StoreInfo, List<byte>> cctorOnStore = (StoreInfo s, List<byte> w) =>
			{
				GoogleV1Inspector.StaticCctorOnStore(s, fieldNames);
			};
			// Walk static constructor method.
			MethodWalker.WalkMethod(staticConstructor, cctorOnCall, cctorOnStore);

			// Extract direct fields from the serialize method of the class.
			var serializer = _subject.Methods.First(GoogleV1Inspector.MatchSerializeMethod);
			var localReferences = new List<TypeDefinition>();
			Action<CallInfo, List<byte>> serializeOnCall = (CallInfo c, List<byte> w) =>
			{
				GoogleV1Inspector.SerializeOnCall(c, fieldNames, allFields, targetProperties, localReferences);
			};
			Action<StoreInfo, List<byte>> serializeOnStore = (StoreInfo s, List<byte> w) =>
			{
				GoogleV1Inspector.SerializeOnStore(s);
			};
			// Walk static constructor method.
			MethodWalker.WalkMethod(serializer, serializeOnCall, serializeOnStore);


			references = localReferences;
		}

		private void DecompileClass_Google(IRClass target, out List<TypeDefinition> references)
		{
			// Fetch properties of the type..
			var props = GoogleCSInspector.ExtractClassProperties(_subject, out references);
			// Store properties.
			target.Properties = props;

			// Container of all parsed tags.
			List<ulong> tags = new List<ulong>();
			var constructEnumeration = _subject.Methods.Where(GoogleCSInspector.MatchStaticConstructor);
			if (!constructEnumeration.Any())
			{
				throw new ExtractionException("No static constructor found!");
			}
			var construct = constructEnumeration.First();
			Action<CallInfo, List<byte>> cctorOnCall = (CallInfo c, List<byte> w) =>
			{
				GoogleCSInspector.StaticCctorOnCall(c, props, tags);
			};
			Action<StoreInfo, List<byte>> cctorOnStore = (StoreInfo s, List<byte> w) =>
			{
				GoogleCSInspector.StaticCctorOnStore(s, props, tags);
			};
			// Walk static constructor method.
			MethodWalker.WalkMethod(construct, cctorOnCall, cctorOnStore);

			// Extract necesary methods for decompilation.
			var serializeEnumeration = _subject.Methods.Where(GoogleCSInspector.MatchSerializeMethod);
			if (!serializeEnumeration.Any())
			{
				throw new ExtractionException("No serialize method found!");
			}

			// Get serialize method.
			var serialize = serializeEnumeration.First();

			// Handler for serialize oncall action.
			Action<CallInfo, List<byte>> googleSerializeOnCall = (CallInfo info, List<byte> w) =>
			{
				// Just chain the call.
				GoogleCSInspector.SerializeOnCall(info, w, props);
			};

			// Walk serialize method.
			MethodWalker.WalkMethod(serialize, googleSerializeOnCall, null);
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

				// Unless the enum property is already UPPER_SNAKE, convert it to UPPER_SNAKE.
				var propName = field.Name;
				var hasLower = propName.Where(c => char.IsLower(c)).Any();
				if (hasLower)
				{
					// Convert PascalCase to UPPER_SNAKE.
					propName = propName.PascalToSnake();
				}

				// Add a new property to the list for this enum field
				props.Add(new IREnumProperty
				{
					// Straight name copy
					Name = propName,
					// If the enumValue is NOT NULL, use enum value.. else use the integer 0
					Value = (enumValue ?? 0)
				});
			}

			// Save properties
			((IREnum)_constructedSubject).Properties = props;
		}
	}
}
