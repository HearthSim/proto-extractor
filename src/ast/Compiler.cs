using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor
{
    interface Compiler
    {
        bool GenerateFiles(string path, Program codeTree);
    }
}
