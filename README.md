# MTGOSDK

> [!WARNING]
> This project is still under construction and is not production-ready!

This SDK provides common APIs for accessing the **Magic: The Gathering Online (MTGO)** client's game state and player information, as well as internal states of the game engine useful for building tools that can assist with gameplay, such as deck trackers, or for analyzing game data for research purposes.

Refer to the project's [examples](/examples) for sample applications built with the SDK.

For more in-depth information on the SDK's APIs, refer to the project [documentation](/docs).

## Overview

This project consists of three main components:

* [**MTGOInjector**](src/MTGOInjector), a library for remotely inspecting and interacting with the MTGO client.
* [**MTGOSDK**](src/MTGOSDK), a library providing high-level APIs for interacting with the MTGO client.
* [**MTGOSDK.Ref**](src/MTGOSDK.Ref), a library containing internal types used by the MTGO client and SDK.

**MTGOInjector** works by injecting a [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) assembly into the MTGO process. This project bootstraps ClrMD to provide an API to walk the managed heap, inspect objects, and invoke methods (such as getters and setters) on an object reference. These object references are created by compiling IL code from an object's memory address to yield an object instance, allowing for the use of reflection on heap objects as if they were live objects.

> [!NOTE]
> ClrMD is a debugging tool that wraps native COM interfaces exposed by the .NET runtime to explore in-memory snapshots of a running process. This is commonly used for crash analysis, performance profiling, automated debugging, and security research of .NET applications.

**MTGOSDK** wraps these objects in a set of methods and classes that provide a safe and intuitive API for interacting with the MTGO client. When running an application with the SDK, the MTGO client is automatically injected with MTGOInjector to provide runtime bindings for these classes. This SDK is optimized for performance and ease of use and is fully typed to provide compile-time safety and intellisense.

**MTGOSDK.Ref** generates reference assemblies for the latest build of MTGO using JetBrain's [Refasmer](https://github.com/JetBrains/Refasmer) tool. These assemblies contain only the public types and members of internal classes from the MTGO client and do not contain any implementation details. As the MTGO client is updated, these assemblies can be regenerated to provide the latest types and members for use in the SDK.

## Building this Project

This project requires the [.NET Framework 4.8 SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48) and [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) to be installed with Visual Studio 2017 or newer. These can also be installed separately with the above installers or when installing Visual Studio with the [Visual Studio Installer](https://learn.microsoft.com/en-us/visualstudio/install/install-visual-studio?view=vs-2022).

Building this project with [MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild?view=vs-2022) (e.g. when using the msbuild or dotnet CLI) requires [Visual C++ Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/) to be installed. These redistributables are also optionally included with Visual Studio 2015 Update 3 and newer.

To build this project using MSBuild, run the following command from the root of the repository:

```powershell
msbuild SDK.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

## License

This project is licensed under the [Apache-2.0 License](/LICENSE).

## Disclaimer

> [!NOTE]
> This project is protected under U.S., **Section 103(f)** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) protections for reverse-engineering for the purpose of enabling ‘interoperability’.

**Section 12.1(b)** of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) prohibits any modification, reverse engineering, or decompilation of the client '*except to the extent that such restriction is expressly prohibited by applicable law*'. For such purposes protected under **Section 103(f)** of the DMCA, however, this EULA clause is pre-empted and rendered null and void *expressly*.

Usage of this project prohibited by the MTGO EULA and applicable law is not condoned by the project authors. The project authors are not responsible for any consequences of such usage.
