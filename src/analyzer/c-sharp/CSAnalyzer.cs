using Mono.Cecil;
using protoextractor.decompiler.c_sharp;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace protoextractor.analyzer.c_sharp
{
	class CSAnalyzer : DefaultAnalyzer
	{
		/*
		    The CSharp analyzer uses Mono.Cecil for type extraction.
		*/
		// Class used for resolving C# dll dependancies.
		private ReaderParameters _resolverParameters;

		// Cache for all registered classes.
		private Dictionary<TypeDefinition, IRClass> _classCache;
		// Cache for all registered enums.
		private Dictionary<TypeDefinition, IREnum> _enumCache;
		// Queue for all (indirectly) referenced types.
		private List<TypeDefinition> _referencedTypes;

		// Root of the Intermediate Representation of the input.
		private IRProgram _root;

		public CSAnalyzer()
		{
			_classCache = new Dictionary<TypeDefinition, IRClass>(new TypeDefinitionComparer());
			_enumCache = new Dictionary<TypeDefinition, IREnum>(new TypeDefinitionComparer());
			_referencedTypes = new List<TypeDefinition>();
		}

		private void SetupAssemblyResolver()
		{
			// Assemblyresolver locates assembly dependancies for us.
			DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
			// The directory of the actual library is added because most dependancies
			// are located within the same folder.
			resolver.AddSearchDirectory(_path);
			// Prepare a parameter for the loadAssembly call later, see Parse(..).
			_resolverParameters = new ReaderParameters()
			{
				AssemblyResolver = resolver,
			};
		}

		private void AnalyzeAssembly(AssemblyDefinition assembly)
		{
			// All analyzable types can be found at the main module of the assembly.
			var module = assembly.MainModule;
			// Fetch all analyzable types.
			var types = module.Types.Where(ILDecompiler.MatchDecompilableClasses);
			// Sort all types in ascending order.
			// This forces parent types to always be processed before nested types!
			types.OrderBy(x => x.FullName);
			// Decompile all types.
			foreach (var type in types)
			{
				// Decompile the type to IR, see DecompileClass(..).
				DecompileClass(type);
			}
		}

		// Run the decompiler on the given type.
		private IREnum DecompileEnum(TypeDefinition type)
		{
			IREnum reconstructed;
			_enumCache.TryGetValue(type, out reconstructed);
			if (reconstructed != null)
			{
				return reconstructed;
			}

			// Decompile the enum..
			ILDecompiler decompiler = new ILDecompiler(type);

			List<TypeDefinition> references;
			decompiler.Decompile(out references);
			// An enum does NOT make references to other types.

			// Retrieve the reconstructed IR.
			reconstructed = decompiler.ConstructIREnum();

			// Add IR enum to the cache
			_enumCache[type] = reconstructed;

			return reconstructed;
		}

		// Run the decompiler on the given type.
		private IRClass DecompileClass(TypeDefinition type)
		{
			// Proceed to next type if it's already known.
			IRClass reconstructed;
			_classCache.TryGetValue(type, out reconstructed);
			if (reconstructed != null)
			{
				return reconstructed;
			}

			var decompiler = new ILDecompiler(type);

			// Create a list to keep track of all referenced types.
			List<TypeDefinition> references;
			decompiler.Decompile(out references);
			// Store all references to analyze them later.
			_referencedTypes.AddRange(references);

			// Retrieve the reconstructed IR.
			reconstructed = decompiler.ConstructIRClass();

			// Add the IR class to the cache.
			// This must happen BEFORE decompiling private types and references,
			// because otherwise causes (possibly) an infinite loop.
			_classCache[type] = reconstructed;

			// Also handle the private types of the subject.
			DecompilePrivateTypes(type, type);

			return reconstructed;
		}

		// Look recursively inside all private subtypes of the parent.
		// All found enums will be stored under the IRType of 'parent'.
		// Container is the type directly containing the child.
		// eg. ns.class.private_class.private_child_enum.
		private void DecompilePrivateTypes(TypeDefinition container, TypeDefinition parent)
		{
			foreach (var nestedType in container.NestedTypes)
			{
				if (nestedType.IsEnum)
				{
					// Decompile the nested enum..
					// Private types ARE ALSO STORED INSIDE THE CACHE.
					// This way we don't have to do a nested search (in cache) looking for private types..
					var irEnum = DecompileEnum(nestedType);
					// Mark the IREnum as private!
					irEnum.IsPrivate = true;
					irEnum.Parent = _classCache[parent];

					// Make a reference between the parent and this (private) enum.
					_classCache[parent].PrivateTypes.Add(irEnum);
				}
				else if (nestedType.IsClass)
				{
					// This nested type might not be a decompilable class. This because it's not directly
					// taken from ILDecompiler.MatchAnalyzableClasses(..).
					try
					{
						// Decompile and store into cache.
						var irClass = DecompileClass(nestedType);
						// Mark the IRClass as private!
						irClass.IsPrivate = true;
						irClass.Parent = _classCache[parent];

						// Mark a reference between the parent and this class.
						_classCache[parent].PrivateTypes.Add(irClass);
					}
					catch (decompiler.ExtractionException)
					{
						Program.Log.Debug("`{0}` is not a valid Protobuffer class.", nestedType.FullName);
					}

					// Recursively inspect the nested type for private types.
					// This must happen regardless of the decompiling status of the container type.
					DecompilePrivateTypes(nestedType, parent);
				}
			}
		}

		// Converts data from internal caches into the IR Program structure.
		public override IRProgram GetRoot()
		{
			// Create a list of all types to process.
			List<TypeDefinition> classesToProcess = new List<TypeDefinition>(_classCache.Keys);
			List<TypeDefinition> enumsToProcess = new List<TypeDefinition>(_enumCache.Keys);

			// Holds a set of all known namespaces.
			Dictionary<string, IRNamespace> nsSet = new Dictionary<string, IRNamespace>();

			// Until all types are processed, we repeat the same operation.
			while (classesToProcess.Count() > 0)
			{
				// Take the first item from the list.
				var currentType = classesToProcess.ElementAt(0);
				classesToProcess.RemoveAt(0);
				// Find namespace list.
				var nsName = GetNamespaceName(currentType);
				IRNamespace ns;
				nsSet.TryGetValue(nsName, out ns);
				if (ns == null)
				{
					// Construct a shortname of the last namespace part of the full name.
					var lastDotIdx = nsName.LastIndexOf('.');
					var nsShortname = (lastDotIdx != -1) ? nsName.Substring(lastDotIdx + 1) : nsName;
					ns = new IRNamespace(nsName, nsShortname)
					{
						Classes = new List<IRClass>(),
						Enums = new List<IREnum>(),
					};
					nsSet[nsName] = ns;
				}

				// Get the matching IR object.
				var irClass = _classCache[currentType];
				if (irClass.IsPrivate != true && irClass.Parent == null)
				{
					irClass.Parent = ns;
				}
				// Save it into the namespace.
				ns.Classes.Add(irClass);
			}

			// Do basically the same for enums.
			while (enumsToProcess.Count() > 0)
			{
				// Take the first item from the list.
				var currentType = enumsToProcess.ElementAt(0);
				enumsToProcess.RemoveAt(0);
				// Find namespace list.
				var nsName = GetNamespaceName(currentType);
				IRNamespace ns;
				nsSet.TryGetValue(nsName, out ns);
				if (ns == null)
				{
					// Construct a shortname of the last namespace part of the full name.
					var lastDotIdx = nsName.LastIndexOf('.');
					var nsShortname = (lastDotIdx != -1) ? nsName.Substring(lastDotIdx + 1) : nsName;
					ns = new IRNamespace(nsName, nsShortname)
					{
						Classes = new List<IRClass>(),
						Enums = new List<IREnum>(),
					};
					nsSet[nsName] = ns;
				}

				// Get the matching IR object.
				var irEnum = _enumCache[currentType];
				if (irEnum.IsPrivate != true && irEnum.Parent == null)
				{
					irEnum.Parent = ns;
				}
				// Save it into the namespace.
				ns.Enums.Add(irEnum);
			}

			// Generate IR root of all namespaces.
			_root = new IRProgram()
			{
				Namespaces = nsSet.Values.ToList(),
			};
			// Return the program root.
			return _root;
		}

		// Gets the namespace string for the given type.
		private string GetNamespaceName(TypeDefinition t)
		{
			if (t.IsNested)
			{
				// String operations to construct namespace.
				// eg; ns.type/nestedType/nestedEnum => ns = namespace.
				var lastDotIdx = t.FullName.LastIndexOf('.');
				// Idx is 0-indexed, but we don't want to include the dot itself!
				return t.FullName.Substring(0, lastDotIdx);
			}

			// Just return the namespace.
			return t.Namespace;
		}

		public override DefaultAnalyzer Parse()
		{
			Program.Log.OpenBlock("DefaultAnalyzer::Parse()");

			SetupAssemblyResolver();

			List<string> analyzableFiles = GetAnalyzableFileNames();
			foreach (var assemblyFileName in analyzableFiles)
			{
				Program.Log.Info("Processing assembly at location `{0}`", assemblyFileName);
				// Load assembly file from the given location.
				AssemblyDefinition ass = AssemblyDefinition.ReadAssembly(assemblyFileName,
																		 _resolverParameters);
				// And process..
				AnalyzeAssembly(ass);
			}

			// Analyze all referenced types.
			DecompileReferencedTypes();

			// Replace all placeholder references with actual IRTypes.
			UpdatePlaceholderReferences();

			Program.Log.CloseBlock();
			return this;
		}

		private void DecompileReferencedTypes()
		{
			// As long as there are referenced types, we decompile them.
			while (_referencedTypes.Count() > 0)
			{
				// Generate copy of referenced types.
				var refTypes = _referencedTypes.ToList();
				// Clear the list.
				_referencedTypes.Clear();

				// Loop each collected reference.
				// Decompiling each reference will in itself generate more references.
				// That's why the object's referencedList was emptied..
				foreach (var refType in refTypes)
				{
					if (refType.IsEnum)
					{
						DecompileEnum(refType);
					}
					else if (refType.IsClass)
					{
						DecompileClass(refType);
					}
					else
					{
						throw new Exception("Cannot handle this kind of reference!");
					}
				}

			}
		}

		// All classes that have a property that references another have placeholder IR objects.
		// This function will perform a second pass over the type cache and replace the references
		// with actual references to the IR types (class or enum).
		private void UpdatePlaceholderReferences()
		{
			foreach (var kv in _classCache)
			{
				// The value is an IRClass, loop it's properties..
				foreach (var prop in kv.Value.Properties)
				{
					if (prop.Type == PropertyTypeKind.TYPE_REF)
					{
						// Find the actual IR type by the fullName of the placeholder.
						// All (valid) analyzed types MUST BE FOUND inside the cache.
						var referenceName = prop.ReferencedType.FullName;
						var irReference = FindReference(referenceName);
						// Overwrite the reference with the actaul object.
						prop.ReferencedType = irReference;
					}
				}
			}
		}

		// Search the caches for an IRType matching the fullname.
		private IRTypeNode FindReference(string fullName)
		{
			// Create a stub to search dictionaries.
			TypeDefinition stubDef = new TypeDefinition(null, fullName, 0);

			// First search through enum cache..
			IREnum retEnum;
			_enumCache.TryGetValue(stubDef, out retEnum);
			if (retEnum != null)
			{
				return retEnum;
			}

			// Then search through class cache..
			IRClass retClass;
			_classCache.TryGetValue(stubDef, out retClass);
			// Per default, return null .. or the IR class type.
			return retClass;
		}
	}
}
