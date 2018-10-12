using Mono.Cecil;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace protoextractor.decompiler.c_sharp.inspectors
{
	// The current implementation of Protobuffers for C# is a rework of code originally created on the Google
	// version control system. A mirror can be found here: https://github.com/jskeet/protobuf-csharp-port
	class GoogleV1Inspector
	{
		public static bool MatchDecompilableClasses(TypeDefinition t)
		{
			return t.IsClass && t.BaseType != null &&
				(t.BaseType.Name.Equals("ExtendableMessageLite`2") || t.BaseType.Name.Equals("GeneratedMessageLite`2")) &&
				t.NestedTypes.Any(nt => nt.Name.Equals("Builder"));
		}

		public static bool MatchStaticConstructor(MethodDefinition m)
		{
			return (m.IsConstructor && m.IsStatic);
		}

		public static bool MatchDeserializeMethod(MethodDefinition method)
		{
			// [Message]::Builder::MergeFrom(CodedInputStream, ExtensionRegistry)
			return method.Name.Equals("MergeFrom") && method.Parameters.Count == 2 &&
				   method.Parameters[0].ParameterType.Name.Equals("CodedInputStream") &&
				   method.Parameters[0].ParameterType.Name.Equals("ExtensionRegistry");
		}

		public static bool MatchSerializeMethod(MethodDefinition method)
		{
			// [Message]::WriteTo(CodedOutputStream)
			return method.Name.Equals("WriteTo") && method.Parameters.Count == 1 &&
				   method.Parameters[0].ParameterType.Name.Equals("ICodedOutputStream");
		}

		public static List<FieldDefinition> ExtractClassFields(TypeDefinition subjectClass)
		{
			var filteredFields = subjectClass.Fields.Where(field => !field.Name.StartsWith("has") && !field.Name.Contains("Number"));
			return new List<FieldDefinition>(filteredFields);
		}

		public static void StaticCctorOnCall(CallInfo info)
		{
			return;
		}

		public static void StaticCctorOnStore(StoreInfo info, List<string> property_names)
		{
			if (!(info.RawObject is OpenArray))
			{
				return;
			}

			var arrayName = info.Argument;
			var array = info.RawObject as OpenArray;
			if (arrayName.Contains("String"))
			{
				property_names.AddRange(array.Contents.Cast<string>());
			}
		}

		public static void SerializeOnCall(CallInfo info, List<string> property_names, List<FieldDefinition> allFields, List<IRClassProperty> properties, List<TypeDefinition> references)
		{
			if (!info.Method.Name.StartsWith("Write") || info.Method.Name.Equals("WriteUntil"))
			{
				// We are in no relevant method.
				return;
			}

			var fieldNameArg = info.Arguments[2].ToString();
			var bracketIdx = fieldNameArg.LastIndexOf("[");
			var bracketLastIdx = fieldNameArg.LastIndexOf("]");
			var fieldNameRef = UInt32.Parse(fieldNameArg.Substring(bracketIdx + 1, bracketLastIdx - bracketIdx - 1));
			var fieldName = property_names[(int)fieldNameRef];

			var type = info.Method.Name.Substring(5);

			var packedIdx = type.IndexOf("Packed");
			var isPacked = false;
			if (packedIdx > -1)
			{
				Debug.Assert(packedIdx == 0);
				isPacked = true;
				type = type.Substring(6);
			}

			var specificType = InspectorTools.LiteralTypeMapper(type);
			var fieldIdx = (int)info.Arguments[1];

			var label = FieldLabel.REQUIRED;
			if (info.Conditions.Any(c => c.Lhs.Contains("get_Count")))
			{
				label = FieldLabel.REPEATED;
			}
			else if (info.Conditions.Any(c => c.Lhs.Contains("has")))
			{
				label = FieldLabel.OPTIONAL;
			}

			// Construct IR reference placeholder.
			IRTypeNode irReference = null;
			if (specificType == PropertyTypeKind.TYPE_REF)
			{
				TypeReference fieldReference;
				if (info.Method.IsGenericInstance)
				{
					var method = info.Method as GenericInstanceMethod;
					fieldReference = method.GenericArguments[0];
				}
				else
				{
					// Find out definition from arguments
					var cleanFieldName = fieldName.ToLower().Replace("_", "");
					var fieldRef = allFields.OrderBy(f => f.Name.Length).First(field => field.Name.ToLower().Contains(cleanFieldName));
					fieldReference = fieldRef.FieldType;
				}

				if (label == FieldLabel.REPEATED && fieldReference.IsGenericInstance)
				{
					var genericField = fieldReference as GenericInstanceType;
					fieldReference = genericField.GenericArguments[0];
				}

				var fieldDefinition = fieldReference.Resolve();
				irReference = InspectorTools.ConstructIRType(fieldDefinition);

				Debug.Assert(!fieldDefinition.FullName.Equals("System.UInt32"));

				// And save the reference TYPEDEFINITION for the caller to process.
				references.Add(fieldDefinition);
			}

			var newProperty = new IRClassProperty()
			{
				Name = fieldName,
				Type = specificType,
				ReferencedType = irReference,
				Options = new IRClassProperty.ILPropertyOptions()
				{
					Label = label,
					PropertyOrder = fieldIdx,
					IsPacked = isPacked,
				}
			};
			properties.Add(newProperty);

			return;
		}

		public static void SerializeOnStore(StoreInfo info)
		{
			return;
		}
	}
}
