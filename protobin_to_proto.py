#!/usr/bin/env python
import argparse
import os
import sys
import google.protobuf.descriptor_pb2 as pb2
from google.protobuf.internal.decoder import _DecodeVarint
from google.protobuf.message import DecodeError
import zlib


def is_valid_path(filepath):
    whitelist = set("abcdefghijklmnopqrstuvwxyz0123456789-_/$,.[]()")

    if not isinstance(filepath, str):
        try:
            filepath = str(filepath, "utf8")
        except UnicodeDecodeError:
            return False

    if all(char in whitelist for char in filepath.lower()):
        return True
    return False


def discover_fd_proto_fields():
    known_fields = {key: value for key, value in pb2.FileDescriptorProto.__dict__.items() if
                    key.endswith("FIELD_NUMBER")}
    return list(known_fields.values())


class FileDescriptorWalker:
    KNOWN_FIELD_NUMBERS = discover_fd_proto_fields()

    def __init__(self, data: bytearray):
        self.data = data
        self.no_double_fields = [pb2.FileDescriptorProto.NAME_FIELD_NUMBER,
                                 pb2.FileDescriptorProto.PACKAGE_FIELD_NUMBER]
        self.seen_fields = []
        self._size = 0

    def approximate_size(self):
        end = False
        offset = 0
        while not end and offset < len(self.data):
            value, offset = _DecodeVarint(self.data, offset)
            field = (value & 0xfffffff8) >> 3
            wire_type = value & 0x7  # last 3 bits

            # If we have seen certain fields already, we might have entered a next encoded protobuffer.
            if (field not in self.KNOWN_FIELD_NUMBERS) or \
                    (field in self.no_double_fields and field in self.seen_fields):
                end = True
                break

            self.seen_fields.append(field)

            # Varint
            if wire_type == 0:
                _, offset = _DecodeVarint(self.data, offset)
            # 64-bit
            elif wire_type == 1:
                offset += 8
            # Length-delimited (~string/bytes)
            elif wire_type == 2:
                value, offset = _DecodeVarint(self.data, offset)
                offset += value
            # Groups - deprecated
            elif wire_type == 3 or wire_type == 4:
                continue
            # 32-bit
            elif wire_type == 5:
                offset += 4
            else:
                end = True

        self._size = offset

    def get_size(self):
        return self._size


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

    def __init__(self):
        self.out = None
        self.indent_level = 0

    def decompile(self, file, out_dir=".", stdout=False):
        data = file.read()
        file.close()

        # Collect all hidden FileDescriptor protobuffer objects
        descriptors = []

        # Discover wire-encoded FileDescriptorProto's
        print("Checking for wire-encoded proto files!")
        descriptors.extend(self.discover_encoded_file_descriptor(data))
        # Discover GZipped FileDescriptorProto's
        print("Checking for GZIPPED proto files!")
        descriptors.extend(self.discover_gzipped_file_descriptor(data))

        for descriptor in descriptors:
            descriptor_name = descriptor.name
            if stdout:
                self.out = sys.stdout
            else:
                out_file_name = os.path.join(out_dir, descriptor_name)
                out_full_dir = os.path.dirname(out_file_name)
                if not os.path.exists(out_full_dir):
                    os.makedirs(out_full_dir)
                self.out = open(out_file_name, "w")
                print(out_file_name)

            self.indent_level = 0
            self.decompile_file_descriptor(descriptor)

    def discover_encoded_file_descriptor(self, data):
        descriptors = []

        proto_bytes = ".proto".encode()
        proto_namespace = ".protobuf".encode()

        offset = 0
        while offset < len(data):
            try:
                p = data.index(proto_bytes, offset)
                # Next iteration must start after p!
                offset = p + 1

                if data[p:p + len(proto_namespace)] == proto_namespace:
                    continue

                # Backtrack_range is allowed to flow back into previous message
                backtrack_range = range(150)
                for diff in backtrack_range:
                    try:
                        varint_pos = p - diff
                        value, str_start_idx = _DecodeVarint(data, varint_pos)
                        pathlength = p + len(proto_bytes) - str_start_idx
                        filepath = data[str_start_idx:str_start_idx + pathlength]

                        # Hard constraints:
                        #       pathlength is never allowed to be less than zero!
                        #       filepath MUST always contain valid characters!
                        #
                        # Result should match the length of the entire filepath string
                        if pathlength < 0 or \
                                not is_valid_path(filepath) or \
                                        value != pathlength:
                            continue

                        # Locate the index of the Tag which indicates the filename field.
                        # This is limited to 1 byte since we know FileDescriptorProto has less than 2^4 fields
                        proto_start_offset = varint_pos - 1
                        proto_stream = data[proto_start_offset:]

                        proto_walker = FileDescriptorWalker(proto_stream)
                        proto_walker.approximate_size()
                        approx_size = proto_walker.get_size()

                        for adj_size in range(approx_size, 0, -1):
                            try:
                                slice = proto_stream[:adj_size]
                                descriptor = pb2.FileDescriptorProto.FromString(slice)

                                # Unnamed proto's are malformed and we don't want them!
                                if len(descriptor.name) > 0:
                                    print("HIT `%s`" % descriptor.name)
                                    descriptors.append(descriptor)
                                break
                            except DecodeError:
                                pass

                        break
                    except DecodeError:
                        pass
            except ValueError:
                # End of file reached
                break

        return descriptors

    def discover_gzipped_file_descriptor(self, data):
        descriptors = []

        # Magic string / ID
        # Including 'deflate' compression method
        gzip_header = bytearray.fromhex('1f8b08')

        offset = 0
        while offset < len(data):
            try:
                p = data.index(gzip_header, offset)
                # Next iteration must start after p!
                offset = p + 1

                # Setup new decompression system
                decompressed_data = bytearray()
                d = zlib.decompressobj(zlib.MAX_WBITS | 32)
                inner_offset = p
                try:
                    while not d.eof:
                        # Slice data per 64 bytes
                        slice = data[inner_offset:inner_offset + 64]
                        inner_offset += len(slice)
                        d_data = d.decompress(slice)
                        decompressed_data.extend(d_data)
                except zlib.error:
                    # Invalid compression block encountered
                    continue

                # Decompressed data should be exact!
                try:
                    # Bytearray MUST be converted to a bytestring
                    proto_data = bytes(decompressed_data)
                    descriptor = pb2.FileDescriptorProto.FromString(proto_data)
                    # Unnamed proto's are malformed and we don't want them!
                    if len(descriptor.name) > 0:
                        print("HIT `%s`" % descriptor.name)
                        descriptors.append(descriptor)
                except DecodeError:
                    pass

                # Test remaining data and calculate next offset
                remaining_data = d.unused_data
                offset = inner_offset - len(remaining_data)

            except ValueError:
                # End of file reached
                break

        return descriptors

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
        if field.type in (pb2.FieldDescriptorProto.TYPE_MESSAGE,
                          pb2.FieldDescriptorProto.TYPE_ENUM):
            type_name = field.type_name
        else:
            type_name = self.type_map[field.type]

        # build basic field string with label name
        field_str = "%s %s %s = %d" % (self.label_map[field.label], type_name,
                                       field.name, field.number)

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
        self.write("rpc %s (%s) returns (%s);\n" %
                   (method.name, method.input_type, method.output_type))

    def write(self, str):
        self.out.write("\t" * self.indent_level)
        self.out.write(str)


if __name__ == "__main__":
    app = ProtobinDecompiler()

    parser = argparse.ArgumentParser()
    parser.add_argument(
        "infiles", nargs="+", type=argparse.FileType("rb"), default=sys.stdout)
    parser.add_argument("-o", dest="outdir", help="output directory")
    args = parser.parse_args(sys.argv[1:])

    in_files = args.infiles
    stdout = args.outdir is None

    for file in in_files:
        app.decompile(file, args.outdir, stdout)
