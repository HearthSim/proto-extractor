#!/usr/bin/env python
import argparse
import os
import sys
import google.protobuf.descriptor_pb2 as pb2


class ProtobinDecompiler:
	label_map = {
		pb2.FieldDescriptorProto.LABEL_OPTIONAL: "optional",
		pb2.FieldDescriptorProto.LABEL_REQUIRED: "required",
		pb2.FieldDescriptorProto.LABEL_REPEATED: "repeated"
	}

	type_map = {
		pb2.FieldDescriptorProto.TYPE_DOUBLE: "double",
		pb2.FieldDescriptorProto.TYPE_FLOAT: "float",
		pb2.FieldDescriptorProto.TYPE_INT64: "int64",
		pb2.FieldDescriptorProto.TYPE_UINT64: "uint64",
		pb2.FieldDescriptorProto.TYPE_INT32: "int32",
		pb2.FieldDescriptorProto.TYPE_FIXED64: "fixed64",
		pb2.FieldDescriptorProto.TYPE_FIXED32: "fixed32",
		pb2.FieldDescriptorProto.TYPE_BOOL: "bool",
		pb2.FieldDescriptorProto.TYPE_STRING: "string",
		pb2.FieldDescriptorProto.TYPE_BYTES: "bytes",
		pb2.FieldDescriptorProto.TYPE_UINT32: "uint32",
		pb2.FieldDescriptorProto.TYPE_SFIXED32: "sfixed32",
		pb2.FieldDescriptorProto.TYPE_SFIXED64: "sfixed64",
		pb2.FieldDescriptorProto.TYPE_SINT32: "sint32",
		pb2.FieldDescriptorProto.TYPE_SINT64: "sint64"
	}

	def decompile(self, file, out_dir=".", stdout=False):
		data = file.read()
		file.close()
		descriptor = pb2.FileDescriptorProto.FromString(data)

		self.out = None
		if stdout:
			self.out = sys.stdout
		else:
			out_file_name = os.path.join(out_dir, descriptor.name)
			out_full_dir = os.path.dirname(out_file_name)
			if not os.path.exists(out_full_dir):
				os.makedirs(out_full_dir)
			self.out = open(out_file_name, "w")
			print(out_file_name)

		self.indent_level = 0
		self.decompile_file_descriptor(descriptor)

	def decompile_file_descriptor(self, descriptor):
		# deserialize package name and dependencies
		if descriptor.HasField("package"):
			self.write("package %s;\n" % descriptor.package)

		for dep in descriptor.dependency:
			self.write("import \"%s\";\n" % dep)

		self.write("\n")

		# enumerations
		for enum in descriptor.enum_type:
			self.decompile_enum_type(enum)

		# messages
		for msg in descriptor.message_type:
			self.write("\n")
			self.decompile_message_type(msg)

		# services
		for service in descriptor.service:
			self.write("\n")
			self.decompile_service(service)

	def decompile_message_type(self, msg):
		self.write("message %s {\n" % msg.name)
		self.indent_level += 1

		# deserialize nested messages
		for nested_msg in msg.nested_type:
			self.decompile_message_type(nested_msg)

		# deserialize nested enumerations
		for nested_enum in msg.enum_type:
			self.decompile_enum_type(nested_enum)

		# deserialize fields
		for field in msg.field:
			self.decompile_field(field)

		# extension ranges
		for range in msg.extension_range:
			end_name = range.end
			if end_name == 0x20000000:
				end_name = "max"
			self.write("extensions %s to %s;\n" % (range.start, end_name))

		# extensions
		for extension in msg.extension:
			self.decompile_extension(extension)

		self.indent_level -= 1
		self.write("}\n")

	def decompile_extension(self, extension):
		self.write("extend %s {\n" % extension.extendee)
		self.indent_level += 1

		self.decompile_field(extension)

		self.indent_level -= 1
		self.write("}\n")

	def decompile_field(self, field):
		# type name is either another message or a standard type
		type_name = ""
		if field.type in (pb2.FieldDescriptorProto.TYPE_MESSAGE, pb2.FieldDescriptorProto.TYPE_ENUM):
			type_name = field.type_name
		else:
			type_name = self.type_map[field.type]

		# build basic field string with label name
		field_str = "%s %s %s = %d" % (self.label_map[field.label], type_name, field.name, field.number)

		# add default value if set
		if field.HasField("default_value"):
			def_val = field.default_value
			# string default values have to be put in quotes
			if field.type == pb2.FieldDescriptorProto.TYPE_STRING:
				def_val = "\"%s\"" % def_val
			field_str += " [default = %s]" % def_val
		field_str += ";\n"
		self.write(field_str)

	def decompile_enum_type(self, enum):
		self.write("enum %s {\n" % enum.name)
		self.indent_level += 1

		# deserialize enum values
		for value in enum.value:
			self.write("%s = %d;\n" % (value.name, value.number))

		self.indent_level -= 1
		self.write("}\n")

	def decompile_service(self, service):
		self.write("service %s {\n" % service.name)
		self.indent_level += 1

		for method in service.method:
			self.decompile_method(method)

		self.indent_level -= 1
		self.write("}\n")

	def decompile_method(self, method):
		self.write("rpc %s (%s) returns (%s);\n" % (method.name, method.input_type, method.output_type))

	def write(self, str):
		self.out.write("\t" * self.indent_level)
		self.out.write(str)


if __name__ == "__main__":
	app = ProtobinDecompiler()

	parser = argparse.ArgumentParser()
	parser.add_argument("infiles", nargs="*", type=argparse.FileType("rb"), default=sys.stdout)
	parser.add_argument("-o", dest="outdir", help="output directory")
	args = parser.parse_args(sys.argv[1:])

	in_files = args.infiles
	stdout = args.outdir is None

	for file in in_files:
		app.decompile(file, args.outdir, stdout)
