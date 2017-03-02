using protoextractor.IR;
using System.Collections.Generic;
using System.Linq;

namespace protoextractor.processing
{
	class NamespacePackager
	{
		/*
		    This class tries to group multiple namespaces in packages.
		    The grouping algorithm checks for shared substrings in namespace's fullnames.
		*/

		private IRProgram _program;

		// Cache of all generated names for each namespace object.
		private Dictionary<IRNamespace, string> _packagedNSNames;

		public NamespacePackager(IRProgram program)
		{
			_program = program;
			_packagedNSNames = new Dictionary<IRNamespace, string>();
		}

		public IRProgram Process()
		{
			// Generate packages for each namespace.
			ProcessPackageNames();

			// Update each namespace (and containing types).
			UpdateNamespaces();

			return _program;
		}

		private void ProcessPackageNames()
		{
			_packagedNSNames.Clear();

			// Generate copy of all namespace names because we manipulate the names while looping.
			// This could influence the naming process for the last namespaces.
			var originalNamespaceName = _program.Namespaces.Select(x => x.FullName);

			foreach (var ns in _program.Namespaces)
			{
				// Name of the namespace we are comparing.
				var nsName = ns.FullName;
				// Character count of the largest substring we matched with another name.
				var highestMatchCount = 0;

				foreach (var cmpNSName in originalNamespaceName)
				{
					if (nsName.Equals(cmpNSName))
					{
						continue;
					}

					// Compare both namespaces and return a substring that matches both names,
					// starting from index 0.
					var str = NamespacePackagerHelper.LongestmatchingSubstring(nsName, cmpNSName);
					var matchLength = str.Count();

					// We don't want occurrences by chance, so any matching string with a count
					// below 3 characters is not recorded!
					if (matchLength < 3)
					{
						continue;
					}

					// We don't want half words, so we cut after the last DOT character (if found).
					var lastDotIDx = str.LastIndexOf('.');
					matchLength = (lastDotIDx != -1) ? lastDotIDx : matchLength;

					// Store the amount of characters matched between both names.
					if (matchLength > highestMatchCount)
					{
						highestMatchCount = matchLength;
					}
				}

				// Build package name for the current namespace.
				var packageSuffix = (highestMatchCount == 0) ? nsName : nsName.Substring(0,
									highestMatchCount).Trim('.');
				// Append the shortname of the namespace, resulting in the full name.
				var fullName = packageSuffix + "." + ns.ShortName;
				// Remove repeating sequences of characters.
				fullName = RemoveRepeatingSequences(fullName);

				// Save the full name
				_packagedNSNames[ns] = fullName;
			}
		}

		private string RemoveRepeatingSequences(string input)
		{
			List<int> removePieceIdx = new List<int>();
			// Cut namespace into parts.
			var pieces = new List<string>(input.Split('.'));
			// Check each part if it's repeated.. (if the previous part is the same as the last)
			for (int i = 1; i < pieces.Count; i++)
			{
				if (pieces[i - 1].Equals(pieces[i]))
				{
					// Store the index for later removal.
					removePieceIdx.Add(i);
				}
			}

			// Remove repeating pieces.
			int removeCounter = 0;
			foreach (var idx in removePieceIdx)
			{
				// Remove piece at the given index.
				// RemoveCounter is subtracted because the list shrinks per removed item!
				pieces.RemoveAt(idx - removeCounter);
				// Increase removecounter to not remove pieces out of bounds.
				removeCounter++;
			}

			// Implode pieces again and return.
			return string.Join(".", pieces);
		}

		private void UpdateNamespaces()
		{
			// Loop each namespace and update the fullnames.
			foreach (var ns in _program.Namespaces)
			{
				// Get the new namespace name from the cache.
				var newNSName = _packagedNSNames[ns];
				// Save a reference to the old one.
				var oldNSName = ns.FullName;
				// Update new name.
				ns.FullName = newNSName;

				// For each containing type, update it's fullname.
				// We must replace the old value, because private types can't be reconstruced
				// by concatenating the new NS Name with the ShortName of the type!
				foreach (var irClass in ns.Classes)
				{
					var newClassName = irClass.FullName.Replace(oldNSName, newNSName);
					irClass.FullName = newClassName;
				}

				foreach (var irEnum in ns.Enums)
				{
					var newEnumName = irEnum.FullName.Replace(oldNSName, newNSName);
					irEnum.FullName = newEnumName;
				}
			}
		}

	}

	public static class NamespacePackagerHelper
	{
		// Returns the longest substring that matches 2 or more types of the given list.
		// The substrings are taken from the namespaces of each given type!
		// The substring always starts from index 0 for each fullname.
		// It's an heuristic for the 'longest matching substring problem'.
		public static string LongestmatchingSubstring(string subject, string matcher)
		{
			// Loop all string characters..
			int i;
			var strOne = subject;
			var strCmp = matcher;
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

			return strOne.Substring(0, i);
		}

	}
}
