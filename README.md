<h1>
    <img
      align="top"
      src="/assets/Logo.png"
      height="36"
      alt="MTGOSDK logo"
    />
  MTGOSDK
</h1>

> [!WARNING]
> This project is still under construction and is not production-ready!

This SDK provides common APIs for accessing the **Magic: The Gathering Online (MTGO)** client's game state and player information, as well as internal states of the game engine useful for building tools that can assist with gameplay, such as deck trackers, or for analyzing game data for research purposes.

Refer to the project's [examples](/examples) for demo applications built with the SDK.

For more in-depth information on the SDK's APIs, refer to the project [documentation](/docs).

## Overview

This project consists of four main components:

* [**MTGOSDK**](MTGOSDK), a library providing high-level APIs for interacting with the MTGO client.
* [**MTGOSDK.MSBuild**](MTGOSDK.MSBuild), a MSBuild library for design/compile-time code generation of the SDK.
* [**MTGOSDK.Ref**](MTGOSDK.Ref), a library containing internal types used by the MTGO client and SDK.
* [**MTGOSDK.Win32**](MTGOSDK.Win32), a library containing Win32 API definitions used by the SDK.

**MTGOSDK** works by injecting the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) assembly into the MTGO process, bootstrapping it to provide an API to inspect object references and invoke methods from process memory. These object references are compiled from IL code using an object's memory address to enable reflection on heap objects as if they were live objects. These are used for higher-level abstractions of MTGO functions that perform atomic RPC calls to the client for efficient caching and revalidation. This SDK is optimized for performance and ease of use and is fully typed to provide compile-time safety and intellisense.

**MTGOSDK.MSBuild** is a MSBuild library that manages the code-generation of the SDK. This is used to generate the SDK's API bindings and reference assemblies for the latest builds of MTGO. These assemblies contain only the public types and members of internal classes from the MTGO client and do not contain any implementation details or private code. As the MTGO client is updated, these assemblies can be regenerated to provide the latest types and members for use in the SDK.

**MTGOSDK.Ref** bootstraps the code-generation process by ensuring that MSBuild targets are available for the SDK project to reference. Various metadata like the client version is extracted at build-time, which can be used to bootstrap version-specific targets. This project is also an optional build target that can be used independently to generate the reference assemblies for the latest build of MTGO. This library is not intended to be used directly by consumers of the SDK.

**MTGOSDK.Win32** is a library containing Win32 API definitions and helper functions used by the SDK. These are used to provide a more idiomatic C# API for Win32 functions and to ensure consistent API behavior across different versions of Windows. Additinonally, this library serves as a reference for using Win32 APIs that are not yet implemented as part of the .NET Framework. This library is not intended to be used directly by consumers of the SDK.

## Building this Project

This project requires the [.NET Framework 4.8 SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48) and [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) to be installed with Visual Studio 2017 or newer. These can also be installed separately with the above installers or when installing Visual Studio with the [Visual Studio Installer](https://learn.microsoft.com/en-us/visualstudio/install/install-visual-studio?view=vs-2022).

Building this project with [MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild?view=vs-2022) (e.g. when using the msbuild or dotnet CLI) requires [Visual C++ Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/) to be installed. These redistributables are also optionally included with Visual Studio 2015 Update 3 and newer.

To build this project using MSBuild, run the following commands from the root of the repository:

```powershell
msbuild Ref.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
msbuild SDK.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

## License

This project is licensed under the [Apache-2.0 License](/LICENSE).

## Disclaimer

> [!NOTE]
> This project is protected under U.S., **Section 103(f)** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) protections for reverse-engineering for the purpose of enabling ‘interoperability’.

**Section 12.1(b)** of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) prohibits any modification, reverse engineering, or decompilation of the client '*except to the extent that such restriction is expressly prohibited by applicable law*'. For such purposes protected under **Section 103(f)** of the DMCA, however, this EULA clause is pre-empted and rendered null and void *expressly*.

Usage of this project prohibited by the MTGO EULA and applicable law is not condoned by the project authors. The project authors are not responsible for any consequences of such usage.
