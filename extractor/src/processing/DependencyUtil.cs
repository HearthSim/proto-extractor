using System.Collections.Generic;
using System.Linq;

namespace protoextractor.processing
{
	abstract class DependencyUtil
	{
		// Returns the longest substring that matches 2 or more types of the given list.
		// The substrings are taken from the namespaces of each given type!
		// The substring always starts from index 0 for each fullname.
		public static string LongestmatchingSubstring(List<string> strList)
		{
			var longestMatch = 0;
			string longestSubstring = "";

			// Loop each type.
			foreach (var mStr in strList)
			{
				// Check against each other type.
				foreach (var cmpType in strList)
				{
					if (cmpType == mStr)
					{
						continue;
					}

					// Loop all string characters..
					int i;
					var strOne = mStr;
					var strCmp = cmpType;
					for (i = 0; i < strOne.Count() && i < strCmp.Count(); i++)
					{
						var c1 = strOne[i];
						var c2 = strCmp[i];
						// If mismatch, return.
						if (!c1.Equals(c2))
						{
							break;
						}
					}

					// 'i' contains the amount of characters that match.
					// Only update the longestSubstring if we found a longer string that matches.
					if (i > longestMatch)
					{
						// 0-indexed substring that matches both 'type' and 'cmptype'.
						longestSubstring = strOne.Substring(0, i);
					}
				}
			}

			return longestSubstring;
		}
	}
}
