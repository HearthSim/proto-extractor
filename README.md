# proto-extractor

A C# program to extract [Protocol Buffer definitions](https://developers.google.com/protocol-buffers/)
compiled with [Google Protos](https://github.com/google/protobuf)
or [SilentOrbit](https://silentorbit.com/protobuf/).


# Code formatting

This project uses [AStyle](http://astyle.sourceforge.net/) to keep it's contents formatted.
Take the following steps to format all source code:

1. Download [AStyle](http://astyle.sourceforge.net/)
2. If needed, compile, and add the binary to your PATH variable
3. Run the formatter on all \*.cs files with the formatting options file found in the root of the repo.
When using the recursive option, don't let your terminal expand the wildcard. 
AStyle is capable of handling the wildcard itself.
eg; ```astyle.exe --options=hearthsim_codestyle.ini --recursive "./*.cs"```

# Usage

Compile the program.
Give it the library files you want to decompile and if you want proto2 or proto3 syntax.

The program will do the following actions automatically:

* resolve circular dependancies;
* resolve name collisions.

usage example: 
```bash
proto-extractor --libPath "%HS_LOCATION%\Hearthstone_Data\Managed" 
--outPath "./proto-out" 
"%HS_LOCATION%\Hearthstone_Data\Managed\Assembly-CSharp.dll" 
"%HS_LOCATION%\Hearthstone_Data\Managed\Assembly-CSharp-firstpass.dll" 
```

## Options

### Automatic packaging

The option `--automatic-packaging` will try to group namespaces under the same namespace if their names show similarities. The used algorithm is longest substring matching, with anchorpoint at the beginning of the string. Half-words are cut to the nearest namespace component.

### Manual packaging

The option `--manual-package-file "PATH-TO-INI-FILE"` can be used manually move content of namespaces or specific types to other/new namespaces. See the file `stove-proto-packaging.ini` in the root of the repo for examples. It's important to keep the order of processing algorithms in mind!

## Order of processing algorithms

The execution order of processing algorithms is always as follows:

1. Resolve circular dependancies;
2. Automatic packaging of namespaces;
3. Manual packaging of namespaces;
4. Resolving name collisions.

This order takes both full control and automatisation in consideration.

# License

proto-extractor is licensed under the terms of the MIT license.
The full license text is available in the `LICENSE` file.


# Community

proto-extractor is a [HearthSim](http://hearthsim.info) project. All development
happens on our IRC channel `#hearthsim` on [Freenode](https://freenode.net).

Contributions are welcome. Make sure to read through the `CONTRIBUTING.md` first.
