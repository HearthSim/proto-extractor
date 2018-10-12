using System;
using System.Collections.Generic;
using System.IO;

namespace protoextractor.util
{
	/// <summary>
	/// Implementation that supports INI fileparsing to a certain degree.
	/// This class should be replaced with a mature library when one is available
	/// (eg ini-parser from rickyah when the project is finished being ported to 
	/// net standard)
	/// </summary>
	class IniParser
	{
		// Use special rules to make sure no double key names are registered.
		private static StringComparer _stringComparer;

		private readonly Dictionary<string, Dictionary<string, string>> _container;

		private IniParser(Dictionary<string, Dictionary<string, string>> container)
		{
			_container = container;
		}

		public static IniParser FromFile(string fileName)
		{
			if(_stringComparer == null) _stringComparer = StringComparer.CurrentCultureIgnoreCase;

			if (!File.Exists(fileName)) return null;
			Dictionary<string, Dictionary<string, string>> container = Parse(fileName);

			return new IniParser(container);
		}

		private static Dictionary<string, Dictionary<string, string>> Parse(string fileName)
		{
			var iniContainer = new Dictionary<string, Dictionary<string, string>>(_stringComparer);
			string[] lines = File.ReadAllLines(fileName);

			// Setup default namespace. All key-values declared before a namespace is encountered
			// are stored there.
			var defaultSection = new Dictionary<string, string>(_stringComparer);
			iniContainer[""] = defaultSection;

			Dictionary<string, string> currentSection = defaultSection;
			int lineNo = 0;
			foreach (string l in lines)
			{
				lineNo++;

				string line = l.Trim();
				if (line.Length == 0 || line.StartsWith(";")) continue;

				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					// Found a new section.
					string sectionKey = line.Substring(1, line.Length - 2);
					if (iniContainer.ContainsKey(sectionKey))
					{
						string msg = string.Format("(r{0})Section duplicate found: `{1}`", lineNo, sectionKey);
						throw new Exception(msg);
					}

					var newSection = new Dictionary<string, string>(_stringComparer);
					iniContainer[sectionKey] = newSection;
					currentSection = newSection;
					continue;
				}

				int eqIdx = line.IndexOf("=");
				if (eqIdx == -1)
				{
					string msg = string.Format("(r{0})Invalid line - `=` wasn't found!", lineNo);
					throw new Exception(msg);
				}
				else
				{
					string keyName = line.Substring(0, eqIdx);
					string value = line.Substring(eqIdx + 1);
					if (currentSection.ContainsKey(keyName))
					{
						string msg = string.Format("(r{0})Duplicate key found: `{1}`", lineNo, keyName);
						throw new Exception(msg);
					}

					currentSection[keyName] = value;
				}
			}

			return iniContainer;
		}

		public Dictionary<string, string> GetSection(string sectionName)
		{
			if(_container.ContainsKey(sectionName))
			{
				return _container[sectionName];
			} else
			{
				return new Dictionary<string, string>(_stringComparer);
			}
		}

		public Dictionary<string, string> this[string idx]
		{
			get { return GetSection(idx); }
		}
	}
}
