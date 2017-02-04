using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.compiler
{
    abstract class DefaultCompiler
    {
        // IR of the input data.
        protected IR.IRProgram _program;
        // Location where the compiled output has to be written.
        protected string _path;

        // The name of the file which will contain the whole dumped IR program.
        protected static string _dumpFileName = "dump.proto";

        // If TRUE, this object will dump the whole program to a single file.
        public bool DumpMode { get; set; }

        // If TRUE, a package-style folder structure will be generated.
        // If FALSE, all proto files will be written directly under '_path'.
        public bool PackageStructured { get; set; }

        // Forces accepting an IR program.
        public DefaultCompiler(IR.IRProgram program)
        {
            _program = program;
            DumpMode = false;
            PackageStructured = true;
        }

        // The directory where all files will be written to.
        public DefaultCompiler SetOutputPath(string path)
        {
            _path = path;

            return this;
        }

        // Compiles the IR program to the target format.
        abstract public void Compile();
    }
}
