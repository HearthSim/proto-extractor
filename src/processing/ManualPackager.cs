using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IniParser;
using IniParser.Model;

namespace protoextractor.processing
{
	class ManualPackager : DefaultProcessor
	{
		/*
		    This class repackages each type into a namespace, as dictated by the provided types file.
		    Types not declared by the types file will remain in their original position.
		*/

		private static StringComparison CASE_INSENSITIVE = StringComparison.OrdinalIgnoreCase;

		// INI file indicating where each input type will be moved to.
		private string _typesFile;

		// Contains all loaded configuration data.
		private IniData _confContents;

		public ManualPackager(IRProgram program, string typesFile) : base(program)
		{
			_typesFile = typesFile;
		}

		public override IRProgram Process()
		{
			Program.Log.OpenBlock("ManualPackager::Process()");

			if (!ProcessINIFile())
			{
				throw new Exception("INI file could not be processed!");
			}

			// Relocate (move) types according to matching rules.
			ProcessRelocation();
			// Remove all empty namespaces.
			PurgeEmptyNamespaces();

			Program.Log.CloseBlock();
			return _program;
		}

		private bool ProcessINIFile()
		{
			var parser = new FileIniDataParser();
			_confContents = parser.ReadFile(_typesFile);

			return true;
		}

		private void ProcessRelocation()
		{
			// Moves types first because they are handpicked.
			var typeList = _confContents["types"];
			if (typeList.Count > 0)
			{
				RelocateTypes(typeList);
			}

			var nsList = _confContents["namespaces"];
			if (nsList.Count > 0)
			{
				RelocateNamespaces(nsList);
			}
		}

		private void RelocateNamespaces(KeyDataCollection nsList)
		{
			foreach (var nsMapping in nsList)
			{
				var targetNSName = nsMapping.Value;
				var sourceNSName = nsMapping.KeyName;

				// First look for all source namespaces.
				// Force instant execution instead of deferring to the foreach loop (first moment of collection query).
				var sourceNSEnumeration = _program.Namespaces.Where(ns => ns.FullName.Equals(
																		sourceNSName, CASE_INSENSITIVE)).ToList();

				// Don't create a new namespace if there is nothing to move.
				if (sourceNSEnumeration.Count == 0)
				{
					Program.Log.Warn("No source namespaces found for match `{0}`, there is nothing to move",
									 sourceNSName);
					continue;
				}

				// Create the target namespace.
				var targetNS = _program.GetCreateNamespace(targetNSName);

				Program.Log.Info("Match `{0}`, {1} namespace(s) found as source", sourceNSName,
								 sourceNSEnumeration.Count);

				// Move all types to target namespace.
				foreach (var sourceNS in sourceNSEnumeration)
				{
					Program.Log.Debug("\tMoving contents of namespace `{0}`", sourceNS.OriginalName);

					// List copy must be made because this list will be cleared after moving.
					var movingClasses = sourceNS.Classes.ToList();
					var movingEnums = sourceNS.Enums.ToList();

					targetNS.Classes.AddRange(movingClasses);
					targetNS.Enums.AddRange(movingEnums);

					sourceNS.Classes.Clear();
					sourceNS.Enums.Clear();

					// Update all type names..
					foreach (var irClass in movingClasses)
					{
						irClass.UpdateTypeReferences(targetNS, sourceNS);
					}

					foreach (var irEnum in movingEnums)
					{
						irEnum.UpdateTypeReferences(targetNS, sourceNS);
					}

					Program.Log.Debug("Bulk move from namespace `{0}` to `{1}` succeeded", sourceNS.FullName,
									  targetNSName);
				}
			}
		}

		private void RelocateTypes(KeyDataCollection typeList)
		{
			foreach (var typeEntry in typeList)
			{
				var typeName = typeEntry.KeyName;
				var targetNSName = typeEntry.Value;

				var targetNS = _program.GetCreateNamespace(targetNSName);
				try
				{
					if (_program.MovePublicTypeToNamespace(typeName, targetNS, CASE_INSENSITIVE))
					{
						Program.Log.Debug("Moved type `{0}` to namespace `{1}`", typeName, targetNSName);
					}
					else
					{
						Program.Log.Warn("Type `{0}` was not found", typeName);
					}
				}
				catch (IRMoveException e)
				{
					Program.Log.Warn("Problem occurred while moving type `{0}`: {1}", typeName, e.Message);
				}
			}
		}

		private void PurgeEmptyNamespaces()
		{
			var emptyNSEnumeration = _program.Namespaces.RemoveAll(ns => (ns.Classes.Count == 0 &&
																   ns.Enums.Count == 0));
		}
	}
}
