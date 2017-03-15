using CommandLine;

namespace protoextractor.util
{
	class ExtendedOptions: Options
	{
		[Option("resolve-circular-dependancies", Required = false, DefaultValue = true,
				HelpText = "This switch enables automatic resolving of circular dependancies.")]
		public bool ResolveCircDependancies
		{
			get;
			set;
		}

		[Option("manual-package-file", Required = false, DefaultValue = "",
				HelpText = "Path to the `stove-proto-packaging.ini` file.")]
		public string ManualPackagingFile
		{
			get;
			set;
		}

		[Option("automatic-packaging", Required = false, DefaultValue = false,
				HelpText = "This switch enables automatic packaging of similar namespaces.")]
		public bool AutomaticPackaging
		{
			get;
			set;
		}

		[Option("resolve-name-collisions", Required = false, DefaultValue = true,
				HelpText = "This switch enables resolving name collisions automatically.")]
		public bool ResolveCollisions
		{
			get;
			set;
		}

	}
}
