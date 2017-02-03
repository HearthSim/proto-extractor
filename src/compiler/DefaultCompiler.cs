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

        // Forces accepting an IR program.
        public DefaultCompiler(IR.IRProgram program)
        {
            _program = program;
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
