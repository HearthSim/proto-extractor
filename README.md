# proto-extractor

A C# program to extract [Protocol Buffer definitions](https://developers.google.com/protocol-buffers/)
compiled with [Google Protos](https://github.com/google/protobuf)
or [SilentOrbit](https://silentorbit.com/protobuf/).

# Usage

Compile the program.
Give it the library files you want to decompile and if you want proto2 or proto3 syntax.

The program will do it's thing; resolve circular dependancies and name collisions for you.
It will also try to package different files based on their namespace through substring matching.

usage example: 
```bash
proto-extractor --libPath "%HS_LOCATION%\Hearthstone_Data\Managed" 
--outPath "./proto-out" 
"%HS_LOCATION%\Hearthstone_Data\Managed\Assembly-CSharp.dll" 
"%HS_LOCATION%\Hearthstone_Data\Managed\Assembly-CSharp-firstpass.dll" 
```

## License

proto-extractor is licensed under the terms of the MIT license.
The full license text is available in the `LICENSE` file.


## Community

proto-extractor is a [HearthSim](http://hearthsim.info) project. All development
happens on our IRC channel `#hearthsim` on [Freenode](https://freenode.net).

Contributions are welcome. Make sure to read through the `CONTRIBUTING.md` first.
