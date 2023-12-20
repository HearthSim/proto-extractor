# Proto-extractor

A C# program to extract [Protocol Buffer definitions](https://developers.google.com/protocol-buffers/)
compiled with [Google Protos](https://github.com/google/protobuf)
or [SilentOrbit](https://silentorbit.com/protobuf/).

> The file `protobin_to_proto.py` is discussed [below](#binary-proto-extraction).

# Compilation

There are 2 versions of the extractor project. Both projects have the same purpose, but target a different runtime framework each.

- Extractor; Targets .Net Core. See [1](#extractor).
- Mono-Extractor; Targets Mono. See [2](#mono-extractor).

### Extractor

> Compile and run the `Extractor` project!

You can get the .Net Core compilation tools through 2 channels; 

Download the (CLI) binaries by downloading directly from [the site](https://www.microsoft.com/net/core).
When using the dotnet CLI to build the project, make sure to **restore dependent packages first!**.

WARN; The build artifact is a .dll file that cannot be executed by itself. To run the program it's required to start the dotnet runtime first; `dotnet extractor.dll [ARGUMENTS]`.

*OR*

Update your Visual Studio **2019** installation to include the `.Net Core cross-platform development`, which can be found under section `Other toolsets`.

Using Visual Studio 2019 is recommended because it handles dependencies and builds the project correctly for YOU. 
There is a community version which is [free to install and use](https://www.visualstudio.com/).


### Mono-Extractor

> Compile and run the `Mono-Extractor` project!

You can download the Mono tools from [their site](http://www.mono-project.com/download/). Their `msbuild` executable should automatically restore dependent packages.  
The resulting binary is called 'mono-extractor.exe'.

# Usage

Compile the program.
Give it the library files you want to decompile and if you want proto2 or proto3 syntax.

The program can do the following actions automatically:

* move types between namespaces according to manual config;
* resolve circular dependencies;
* resolve name collisions;
* package types underneath a common namespace;
* generates proto2/proto3 syntax output.

Basic usage example: 
```bash
extractor --libPath "%HS_LOCATION%/Hearthstone_Data/Managed" 
--outPath "./proto-out" 
"%HS_LOCATION%/Hearthstone_Data/Managed/Assembly-CSharp.dll" 
"%HS_LOCATION%/Hearthstone_Data/Managed/Assembly-CSharp-firstpass.dll" 
```

Extended usage example:

> This command is used to generate protobuffer files for [HS proto repository](https://github.com/HearthSim/hsproto)

```bash
extractor --proto3 --debug --resolve-circular-dependencies --automatic-packaging --resolve-name-collisions --outPath "./proto-out" --libPath "%HS_LOCATION%/Hearthstone_Data/Managed" "%HS_LOCATION%/Hearthstone_Data/Managed/Assembly-CSharp.dll" 
"%HS_LOCATION%/Hearthstone_Data/Managed/Assembly-CSharp-firstpass.dll" 
```

> The section `Options` explains all parameters. The executable will write parameter parsing errors on standard outstream.

## Options

### Proto3

The option `--proto3` will use the proto3 compiler to generate .proto files with protobuffer 3 syntax.

WARN; Proto3 syntax is more readable but less accurate. Use Proto2 syntax for emulation purposes.

> Defaults to False.

### Resolve circular dependencies

The option `--resolve-circular-dependencies` will run a processor object that detects and solves circular dependencies for you. Both circular dependencies between types and namespaces are detected.

> Defaults to False.

### Resolve name collisions

The option `--resolve-name-collisions` will run a processor object that detects and solves all kinds of name collisions for you. See the processor code for more details.

> Defaults to False.

### Automatic packaging

The option `--automatic-packaging` will try to group namespaces under the same namespace if their names show similarities. The used algorithm is longest substring matching, with anchorpoint at the beginning of the string. Half-words are cut to the nearest namespace component.

> Defaults to False.

### Manual packaging

The option `--manual-package-file "PATH-TO-INI-FILE"` can be used manually move content of namespaces or specific types to other/new namespaces. See the file `hscode-proto-packaging.ini` in the root of the repo for examples. 

It's important to keep the order of processing algorithms in mind! We shouldn't try to manually compensate the behaviour of the automatic dependency resolver. This has to do with trying to keep a consistent layout of outputted protobuffer files regarding the absolute dependency on the source material.

> Defaults to "" (empty string) -> nothing will happen.

## Order of processing algorithms

The execution order of processing algorithms is always as follows:

1. Manual packaging of namespaces;
2. Resolve circular dependencies;
2. Automatic packaging of namespaces;
3. Resolving name collisions.

# Binary proto extraction

> Use the `protobin_to_proto.py` file!

At the root of the repository you'll find a python3 script that extracts protobuffer files from arbitrary binary files. 

>**This only works on proto files which have been compiled with the Google Protobuffer compiler tool (protoc)!** 
Use the proto-extractor project (see above) for hearthstone related proto files.

## Setup

The only requirement for this script is the [Google Protobuffer package](https://pypi.python.org/pypi/protobuf) which you can install through pip. A quick setup guide is given below.

1. Install virtualenv; `pip3 install virtualenv`
2. Create a virtual environment inside the CWD; `py -3 -m virtualenv ./.env`
3. Activate the environment; `./env/Scripts/activate`
4. Install protobuf package; `pip install protobuf`

# Usage

1. Activate the venv which was setup in the previous section.
2. Run the proto extraction script; `python protobin_to_proto.py -o [out-dir] [inputfile [inputfiles ..]]`
3. Extracted proto files can be found inside [out-dir], the script will output any found, valid filename.

# License

Proto-extractor is licensed under the terms of the MIT license.
The full license text is available in the `LICENSE` file.

# Community

proto-extractor is a [HearthSim](http://hearthsim.info) project. All development
happens on our IRC channel `#hearthsim` on [Freenode](https://freenode.net).

Contributions are welcome. Make sure to read through the `CONTRIBUTING.md` first.
