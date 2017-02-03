using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.analyzer
{
    // The analyzer handles file openins/streams for the decompiler to process.
    abstract class DefaultAnalyzer
    {
        // Location of files to analyze.
        // Defaults to current working directory.
        protected string _path = "";
        // Pattern indicating files to be analyzed.
        // Must be SET!
        protected string _fileNameGlob;

        public DefaultAnalyzer() { }

        // Register the path which will be used to load files from.
        public DefaultAnalyzer SetLibraryPath(string path)
        {
            _path = path;
            return this;
        }

        // Register the filename pattern. All files at the set library path 
        // that match this pattern will be analyzed.
        public DefaultAnalyzer SetFileGlob(string pattern)
        {
            _fileNameGlob = pattern;
            return this;
        }

        // Returns a list of file names that are selected for processing.
        // The resulting file names are absolute paths.
        public List<string> GetAnalyzableFileNames()
        {
            List<string> result = new List<string>();

            if (_fileNameGlob == null)
            {
                throw new InvalidOperationException("Call SetFileGlob(..) first!");
            }

            // Generate list of file names.
            string[] files = Directory.GetFiles(_path, _fileNameGlob);
            // Add them to the list to return.
            result.AddRange(files);

            return result;
        }

        // Parse data from the specified input.
        abstract public DefaultAnalyzer Parse();

        // Returns an Intermediate Representation of the data that got extracted.
        abstract public IR.IRProgram GetRoot();
    }

    public class AnalyzeException : Exception
    {
        // General issue
        public AnalyzeException()
        {

        }

        public AnalyzeException(string message) : base(message)
        {

        }

        public AnalyzeException(string message, Exception other) : base(message, other)
        { }
    }
}
