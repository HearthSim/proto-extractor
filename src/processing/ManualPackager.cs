using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace protoextractor.processing
{
	class ManualPackager : DefaultProcessor
	{
		/*
		    This class repackages each type into a namespace, as dictated by the provided types file.
		    Types not declared by the types file will remain in their original position.
		*/

		// Keys: Namespace name.
		// Values: String to match against program namespaces.
		private Dictionary<string, string> _typeMapper;

		public ManualPackager(IRProgram program) : base(program)
		{
			_typeMapper = new Dictionary<string, string>();
		}

		public override IRProgram Process()
		{
			Program.Log.OpenBlock("ManualPackager::Process()");

			// Relocate (move) types according to matching rules.
			ProcessRelocation();
			// Remove all empty namespaces.
			PurgeEmptyNamespaces();

			Program.Log.CloseBlock();
			return _program;
		}

		// Store a new mapping from program namespaces to the given namespace.
		// nsName will be the name of the new namespace.
		// ! nsName must be a valid namespace name, see IR.IRNamespace.
		// nsFullNameMatcher is the string to match against existing namespaces. Every namespace
		// that matches will have (all) it's types moved to the new namespace.
		// Matching is CASE-SENSITIVE.
		public void AddMapping(string nsName, string nsFullNameMatcher)
		{
			_typeMapper[nsName] = nsFullNameMatcher;
		}

		private void ProcessRelocation()
		{
			foreach (var nsMapping in _typeMapper)
			{
				var nsName = nsMapping.Key;
				var nsMatch = nsMapping.Value;

				// First look for all source namespaces.
				// Force instant execution instead of deferring to the foreach loop (first moment of collection query).
				var sourceNSEnumeration = _program.Namespaces.Where(ns => ns.FullName.Contains(
																		nsMatch)).ToList();
				// Create the target namespace.
				var targetNS = _program.GetCreateNamespace(nsName);

				Program.Log.Info("Match `{0}`, {1} namespace(s) found as source", nsMatch,
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
