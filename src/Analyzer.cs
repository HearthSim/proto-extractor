using protoextractor.extractor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor
{
    // Analyzes all provided types and uses the proper extractor implementation.

    // E is the implemented extractor type.
    abstract class BaseAnalyzer<E>
    {
        // Object that represents the imported.
        protected Program _root;
        // Extractor used for pulling language data.
        protected E _extractor;
        // Location of files to analyze.
        protected string _path;
        // Pattern indicating files to be analyzed.
        protected string _fileNameGlob;

        // Force passing these properties into the analyzer.
        public BaseAnalyzer(E extractor)
        {
            _extractor = extractor;
        }

        // Groups all types under the same namespace.
        public BaseAnalyzer<E> GroupTypes()
        {

            return this;
        }

        // Checks the dependancy relations between all types.
        public BaseAnalyzer<E> CheckDependancies()
        {
            return this;
        }

        // Returns the root of the program; a collection of NameSpace objects.
        public Program GetRoot()
        {
            return _root;
        }

        public Program CreateRoot()
        {
            // Should collect all resolved namespaces,
            // do some validation/parsing
            // and construct a program element.
            return null;
        }

        // Marks the directory 
        public BaseAnalyzer<E> SetPath(string path)
        {
            _path = path;
            return this;
        }

        // The pattern which each filename in the set path must match to be parsed.
        public BaseAnalyzer<E> SetFileGlob(string glob)
        {
            _fileNameGlob = glob;
            return this;
        }

        // Parse the files that match the Glob at the set directory
        public BaseAnalyzer<E> Parse()
        {
            // TODO null check

            // Construct list of filenames to parse
            string[] files = Directory.GetFiles(_path, _fileNameGlob);
            // Execute parsing
            Parse(files);


            return this;
        }

        // Parse the files whose names are given as parameter
        public abstract BaseAnalyzer<E> Parse(string[] files);
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
