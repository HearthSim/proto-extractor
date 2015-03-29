all: main

main:
	mcs main.cs -pkg:dotnet -r:lib/Mono.Cecil.dll -r:lib/Mono.Cecil.Rocks.dll

clean:
	rm -f main.exe
