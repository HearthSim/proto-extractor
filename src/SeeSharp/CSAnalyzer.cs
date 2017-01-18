using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.SeeSharp
{
    class CSAnalyzer : BaseAnalyzer<CSExtractor>
    {
        DefaultAssemblyResolver _resolver = new DefaultAssemblyResolver();

        public CSAnalyzer() : base(GetExtractor())
        {
        }

        public CSAnalyzer(string resolvePath) : base(GetExtractor())
        {
            if (Directory.Exists(resolvePath))
            {
                _resolver.AddSearchDirectory(resolvePath);
            }
            else
            {
                // WARN
            }
        }

        // Creates a new extractor to be passed to base
        private static CSExtractor GetExtractor()
        {
            return new CSExtractor();
        }

        public override BaseAnalyzer<CSExtractor> Parse(string[] files)
        {
            // Construct Cecil loader params
            ReaderParameters loadParams = new ReaderParameters
            {
                AssemblyResolver = _resolver
            };

            foreach (string fileName in files)
            {
                // For each file, check if it exists
                if (!TestFile(fileName))
                {
                    // Error
                    continue;
                }
                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(fileName, loadParams);
                // Setup extractor
                _extractor.SetTargetAssembly(assembly);
                // Run type extractor
                AnalyzeTypes();
            }
            // Construct full program tree
            // Store tree

            return this;
        }

        // Check if the given filename is a file we want to analyze!
        private bool TestFile(string fileName)
        {
            if (!File.Exists(fileName))
            {
                // Warn
                return false;
            }

            if (!Path.GetExtension(fileName).Equals("dll"))
            {
                // Warn
                return false;
            }

            return true;
        }

        // For each analyzable type, we run the extractor
        private void AnalyzeTypes()
        {
            // Analyze libraries
            var types = _extractor.GetAnalyzableTypes();
            foreach (var type in types)
            {
                _extractor.ExtractType(type);
            }
        }


    }
}
