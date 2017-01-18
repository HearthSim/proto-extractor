using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.extractor
{
    // Declaring the public methods for every implemented extractor.
    // An extractor parses given data into the generic programming language structure.
    // The extractor is controlled by the analyzer..

    // Correct flow to use the Extractor would be:
    //  1. Fetch all analyzable types; this does a quick scan on the given input and returns 
    //      potential types for further inspectation.
    //  2. Extract each type.
    //  3. Get all Namespace objects that contain all extracted types.

    // TR = Type Reference; The object used to identify each ILReferenceType (class or enum) at 
    // extraction level
    interface Extractor<TR>
    {
        // Returns an array of all types that COULD be extracted from.
        TR[] GetAnalyzableTypes();

        // Extract this specific type. The returned type could be a class or enum.
        // This method will try to fill in all child elements.
        ILType ExtractType(TR type);

        // Get the namespace for the current type. The returned Namespace ONLY contains the Types
        // that got resolved UP UNTIL THIS MOMENT.
        Namespace GetNamespace(TR type);

        // Returns an array of all known namespaces.
        Namespace[] GetResolvedNamespaces();

        // Try to resolve all properties of the given type.
        void ResolveProperties(TR type);
    }

    // Specific exception thrown by the extractor
    public class ExtractionException : Exception
    {
        // General issue
        public ExtractionException()
        {
        }

        public ExtractionException(ILClassType classD, string message)
        {
        }

        public ExtractionException(ILEnumType enumD, string message)
        {
        }

        public ExtractionException(ILClassProperty property, string message)
        {
        }

        public ExtractionException(ILEnumProperty property, string message)
        {
        }

        public ExtractionException(ILPropertyValue value, string message)
        {
        }

    }
}
