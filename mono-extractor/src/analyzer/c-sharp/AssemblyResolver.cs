using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;

namespace protoextractor.analyzer.c_sharp
{
	// Code ripped from https://github.com/jbevain/cecil/blob/master/Mono.Cecil/BaseAssemblyResolver.cs
	class DirAssemblyResolver : IAssemblyResolver
	{
		private readonly HashSet<string> directories;

		public DirAssemblyResolver()
		{
			// Preset the resolver to always look into the directory of the 
			// specific assembly and within the bin folder next to the specific assembly.
			directories = new HashSet<string>()
			{
				".", "bin"
			};
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
		}

        public void AddSearchDirectory(string directory)
        {
            directories.Add(directory);
        }

        public void RemoveSearchDirectory(string directory)
        {
            directories.Remove(directory);
        }

        AssemblyDefinition GetAssembly(string file, ReaderParameters parameters)
		{
			if (parameters.AssemblyResolver == null)
				parameters.AssemblyResolver = this;

			return ModuleDefinition.ReadModule(file, parameters).Assembly;
		}

		public AssemblyDefinition Resolve(AssemblyNameReference name)
		{
			return Resolve(name, new ReaderParameters());
		}

		public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			AssemblyDefinition assembly = SearchDirectory(name, directories, parameters);
			if (assembly != null)
				return assembly;

			throw new AssemblyResolutionException(name);
		}

		AssemblyDefinition SearchDirectory(AssemblyNameReference name, IEnumerable<string> directories, ReaderParameters parameters)
		{
			// On non-Windows OS'es the libraries can be compiled without extension.
			// We actually rather prefer extensions so keep the no extension test until last.
			string[] extensions = name.IsWindowsRuntime ? new[] { ".winmd", ".dll", ".exe" } : new[] { ".exe", ".dll", "" };
			foreach (string directory in directories)
			{
				foreach (string extension in extensions)
				{
					string file = Path.Combine(directory, name.Name + extension);
					if (!File.Exists(file))
						continue;
					try
					{
						return GetAssembly(file, parameters);
					}
					catch (BadImageFormatException)
					{
						continue;
					}
				}
			}

			return null;
		}
	}
}
