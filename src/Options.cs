using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace protoextractor
{
    class Options
    {
        /* Location for resolving assembly dependancies */
        [Option("libPath", Required = true, HelpText = "The path for resolving input file dependancies.")]
        public string LibraryPath { get; set; }

        /* Directory where proto files will be written to */
        [Option("outPath", Required = true, HelpText = "The path where all compiled proto files will be written to.")]
        public string OutDirectory { get; set; }

        /* Runs the proto3 compiler instead of proto2 */
        [Option("proto3", Required = false, DefaultValue = false, HelpText = "Set this to true to produce proto3 syntax in output files.")]
        public bool Proto3Syntax { get; set; }

        /* List of (absolute path) input filenames */
        [ValueList(typeof(List<string>))]
        public List<string> InputFileName { get; set; }

        [HelpVerbOption]
        public string GetUsage(string verb)
        {
            var help = HelpText.AutoBuild(this);

            help.AddPreOptionsLine("Usage: app.exe [OPTIONS] inputfile1 inputfile2 ..");
            return help;
        }
    }
}
