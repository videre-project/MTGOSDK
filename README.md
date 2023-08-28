# MTGOInjector

> [!WARNING]  
> This project is still under construction and is not production ready!

MTGOInjector is a library for interacting with and inspecting the **Magic: The Gathering Online (MTGO)** client. It provides an API for accessing common information related to tracking game state and player information, as well as internal states of the game engine useful for building tools that can assist with gameplay, such as deck trackers, or for analyzing game data for research purposes.

This works by injecting a [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) assembly into the MTGO process to inspect memory dumps of the client. ClrMD is a debugging tool that wraps native COM interfaces exposed by the .NET runtime. This is commonly used for crash analysis, performance profiling, automated debugging, and security research of .NET applications.

This project bootstraps ClrMD with a fork of [RemoteNET](https://github.com/theXappy/RemoteNET) to provide an API to walk the managed heap, inspect objects, and invoke methods (such as getters and setters) on an object reference. This reference is created by compiling IL code from an object's memory address to yield an object instance (via [indirection](https://en.wikipedia.org/wiki/Indirection)). This allows for the use of reflection on the resulting object to manipulate heap objects as if they were live objects.

## Building this Project

This project requires the [.NET Framework 4.8 SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48) and [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) to be installed with Visual Studio 2017 or newer. These can also be installed separately with the above installers or when installing Visual Studio with the [Visual Studio Installer](https://learn.microsoft.com/en-us/visualstudio/install/install-visual-studio?view=vs-2022).

Building this project with [MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild?view=vs-2022) (e.g. when using the msbuild or dotnet CLI) requires [Visual C++ Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/) to be installed. These redistributables are also optionally included with Visual Studio 2015 Update 3 and newer.

<!-- TODO: Add instructions for building the entire solution with MSBuild. -->

## License

[Apache-2.0 License](/LICENSE).

## Disclaimer

> [!NOTE]
> This project is protected under U.S., **Section 103(f)** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) protections for reverse-engineering for the purpose of enabling ‘cooperative interoperability’.

**Section 12.1(b)** of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) prohibits any modification, reverse engineering, or decompilation of the client '*except to the extent that such restriction is expressly prohibited by applicable law*'. For such purposes protected under **Section 103(f)** of the DMCA, however, this EULA clause is rendered null and void *expressly*.

Usage of this project prohibited by the MTGO EULA and applicable law is not condoned by the project authors. The project authors are not responsible for any consequences of such usage.
