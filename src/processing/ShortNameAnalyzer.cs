using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using protoextractor.IR;

namespace protoextractor.processing
{
    class ShortNameAnalyzer
    {
        private IRProgram _program;

        public ShortNameAnalyzer(IRProgram program)
        {
            _program = program;
        }

        // Changes shortnames of namespaces inplace.
        public IRProgram Process()
        {

            foreach (var mNS in _program.Namespaces)
            {
                // LowestMatchCount contains the amount of characters to remove from the fullName
                // in order to obtain the shortname.
                var lowestMatchCount = 395;
                foreach(var cmpNS in _program.Namespaces)
                {
                    if (mNS == cmpNS) continue;

                    // Compare all namespaces names against each other and keep
                    // track of the amount of characters to subset.
                    List<string> matchList = new List<string>();
                    matchList.Add(mNS.FullName);
                    matchList.Add(cmpNS.FullName);

                    var substrMatch = DependancyUtil.LongestmatchingSubstring(matchList);
                    var matchLength = substrMatch.Count();
                    // Ignore 0, because that wouldn't help us..
                    if(matchLength != 0 && matchLength < lowestMatchCount)
                    {
                        lowestMatchCount = matchLength;
                    }
                }

                // If the variable is untouched or equals the complete full string.
                if(lowestMatchCount == 395 || lowestMatchCount == mNS.FullName.Count())
                {
                    // Keep entire fullname.
                    mNS.ShortName = mNS.FullName;
                } else
                {
                    // Cut chars off of fullname.
                    // Also make sure the name does not end or begin with dots!
                    var shortName = mNS.FullName.Substring(lowestMatchCount).Trim('.');
                    mNS.ShortName = shortName;
                }
                
            }

            return _program;
        }
    }
}
