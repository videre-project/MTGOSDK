# ![MTGOSDK Icon](/assets/icon_36h.png) MTGOSDK

![.NET](https://img.shields.io/badge/dynamic/yaml?label=.NET&labelColor=3f4551&color=8a2be2&prefix=v&query=$.sdk.version&url=https://raw.githubusercontent.com/videre-project/mtgo-sdk/main/global.json)
![MTGO](https://img.shields.io/badge/dynamic/json.svg?logo=data:image/png;base64,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&labelColor=3f4551&color=da460e&label=MTGO&query=$.version&url=https://api.videreproject.com/mtgo/manifest&maxAge=1800)
![Tests](https://img.shields.io/github/actions/workflow/status/videre-project/MTGOSDK/test.yml?label=Tests&labelColor=3f4551)
![Dependencies](https://img.shields.io/librariesio/github/videre-project/MTGOSDK?label=Dependencies&labelColor=3f4551)

<a href="#documentation">Documentation</a> |
<a href="#overview">Overview</a> |
<a href="#installation">Installation</a> |
<a href="#building-this-project">Building</a> |
<a href="#license">License</a> |
<a href="#disclaimer">Disclaimer</a>

| **Package** | **Latest Version** | **About** |
|:--|:--|:--|
| `MTGOSDK` | [![NuGet](https://img.shields.io/nuget/v/MTGOSDK?logo=nuget&labelColor=3f4551&label=NuGet&color=blue)](https://www.nuget.org/packages/MTGOSDK/ "Download MTGOSDK from NuGet.org") | Provides high-level APIs for interacting with the MTGO client. |
| `MTGOSDK.MSBuild` | [![NuGet](https://img.shields.io/nuget/v/MTGOSDK.MSBuild?logo=nuget&labelColor=3f4551&label=NuGet&color=blue)](https://www.nuget.org/packages/MTGOSDK.MSBuild/ "Download MTGOSDK.MSBuild from NuGet.org") | MSBuild library for design/compile-time code generation. |
| `MTGOSDK.Win32` | [![NuGet](https://img.shields.io/nuget/v/MTGOSDK.Win32?logo=nuget&labelColor=3f4551&label=NuGet&color=blue)](https://www.nuget.org/packages/MTGOSDK.Win32/ "Download MTGOSDK.Win32 from NuGet.org") | Contains native Win32 API definitions used by the SDK. |

## What is MTGOSDK?

**MTGOSDK** is a collection of .NET/C# libraries for interfacing with the **Magic: The Gathering Online (MTGO)** client. It provides common APIs for reading from and interacting with the MTGO client in real-time, enabling developers to build tools and applications that can interact with any running instance of MTGO.

MTGOSDK was originally developed to support the [Videre Tracker](https://github.com/videre-project/tracker) project, a real-time game tracker for MTGO. However, the SDK has been designed to be general-purpose and can be used to build a wide variety of applications that interact with MTGO.

This SDK contains several lightweight packages (with minimal dependencies outside of the .NET runtime) designed to be easy to use and integrate into existing applications. They are built entirely in C# on top of the .NET Core runtime and are compatible with .NET 5.0 and later.

## Documentation

The [SDK documentation](/docs/README.md) is organized into the following sections:
- [Getting Started](/docs/getting-started.md) — a quick guide to getting started with the SDK.
- [API Reference](/docs/api-reference.md) — a detailed reference of the SDK's APIs and classes.
- [Architecture](/docs/architecture/README.md) — an overview of the SDK's design and architecture.

Refer to the [FAQ](/docs/FAQ.md) for common questions about the SDK, and the project's [examples](/examples) for demo applications built with the SDK.

## Overview

Below is a brief overview of the packages included in the SDK:

[**MTGOSDK**](https://www.nuget.org/packages/MTGOSDK/ "Download MTGOSDK from NuGet.org") — provides an API that exposes high-level abstractions of MTGO functions to read and manage the state of the client. This works by injecting the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) assembly into the MTGO process and bootstrapping a debugger interface to inspect objects from process memory. These objects are compiled by the .NET runtime to enable reflection on heap objects as if they were live objects.

[**MTGOSDK.MSBuild**](https://www.nuget.org/packages/MTGOSDK.MSBuild/ "Download MTGOSDK.MSBuild from NuGet.org") — a MSBuild library that manages the code-generation of the SDK. This is used to generate the SDK's API bindings and reference assemblies for the latest builds of MTGO. At design-time, this builds reference assemblies containing only the public types and members from the MTGO client. These assemblies can be regenerated as the MTGO client is updated to provide the latest API definitions for use in the SDK.

[**MTGOSDK.Win32**](https://www.nuget.org/packages/MTGOSDK.Win32/ "Download MTGOSDK.Win32 from NuGet.org") — a library containing Win32 API definitions and helper functions used by the SDK. These are used to provide a more idiomatic C# API for Win32 functions and to ensure consistent API behavior across different Windows versions. Additionally, this library serves as a reference for using Win32 APIs that are not yet implemented as part of .NET.

## Installation

> [!NOTE]
> MTGOSDK follows the [Abseil Live at Head philosophy](https://abseil.io/about/philosophy#upgrade-support).
>
> While regular releases are tagged and published on GitHub and NuGet, we recommend [building the SDK from source](#building-this-project) to ensure you have the latest features and bug fixes, and so that newer versions of MTGO can be targeted and validated by the SDK at build-time. This helps catch and fix any [Hyrum's Law](https://www.hyrumslaw.com/) dependency problems on an incremental basis between different versions of MTGO and the SDK.
>
> Follow the [local package feed](#local-package-feed) instructions to reference local builds of MTGOSDK in your project.

The MTGOSDK is available as a NuGet package on the NuGet Gallery, and on GitHub Packages. You can install the package using the NuGet Package Manager in Visual Studio, or with the .NET Core CLI.

To install the MTGOSDK package, you can use any of the below methods:

### With Visual Studio

From within Visual Studio, you can use the NuGet Package Manager GUI to search for and install the MTGOSDK NuGet package. Alternatively, you can use the Package Manager Console to install the package:

```powershell
Install-Package MTGOSDK
```

### With the .NET Core CLI

If you are building with .NET Core command line tools, you can use the below command to add the MTGOSDK package to your project:

```powershell
dotnet add package MTGOSDK
```

### Local Package Feed

When [building the project locally](#building-this-project), you can use the SDK's local package feed to reference the development build. This feed is created by the SDK build process and is created under the `packages` directory.

To reference the local package feed created by the SDK, you can add the following to the `NuGet.config` file in the root of your project:
```xml
<!-- NuGet.config -->
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <!-- Configure the local feed as a package source -->
    <add key="SDK Feed" value="MTGOSDK/packages" />
  </packageSources>
  <packageSourceMapping>
    <!-- Prioritize the local feed over NuGet for SDK packages -->
    <packageSource key="SDK Feed">
      <package pattern="MTGOSDK" />
      <package pattern="MTGOSDK.MSBuild" />
      <package pattern="MTGOSDK.Win32" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

To reference these packages, you can use the `*` wildcard specifier in your project file:

```xml
<!-- Add to your project's .csproj or Directory.Packages.props file: -->
<PackageReference Include="MTGOSDK"
                  Version="*" />
<PackageReference Include="MTGOSDK.MSBuild"
                  Version="*"
                  PrivateAssets="All" />
<PackageReference Include="MTGOSDK.Win32"
                  Version="*" />
```

If using centralized package management, you can also use `1.2.3.0-preview` to
reference local builds for `1.2.3` without using a floating version specifier.

## Building this Project

> [!NOTE]
> This project requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) to be installed with Visual Studio 2022 (v17.13) or newer. This can also be installed in Visual Studio using the [Visual Studio Installer](https://learn.microsoft.com/en-us/visualstudio/install/install-visual-studio?view=vs-2022).

To build this project, run either of the below commands from the root of the repository:

```powershell
# Build using the .NET Core CLI
dotnet build -c Release
```

```powershell
# Build using MSBuild in Visual Studio
msbuild /t:Build /p:Configuration=Release
```

The MTGOSDK project will automatically build reference assemblies for the latest version of MTGO, even if no existing MTGO installation exists. This helps ensure that the SDK is always up-to-date with the latest versions of MTGO.

To build the project in watch-mode (and rebuild the solution as it picks up changes), you can use the `dotnet watch` command targeting the **MTGOSDK** project instead of the solution file:

```powershell
# Automatically rebuild MTGOSDK when file changes are detected
$ dotnet watch --project MTGOSDK/MTGOSDK.csproj build -c Release
```

This will also pick up changes to dependent **MTGOSDK.MSBuild** and **MTGOSDK.Win32** projects as well. As build evaluation is rooted from the MTGOSDK .csproj file, all logs from the build will be stored under the `MTGOSDK/logs` directory.

## Acknowledgements

MTGOSDK's snapshot runtime uses the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) library under the hood. We're grateful to the ClrMD maintainers for their support and their work in providing a powerful library for inspecting and debugging .NET applications.

MTGOSDK's remoting API is also based on an early version of [RemoteNET](https://github.com/theXappy/RemoteNET), which forms the backbone of the SDK's client-server architecture. Together with the [ImpromptuInterface](https://github.com/ekonbenefits/impromptu-interface) library creates fully-typed dynamic proxies for remote objects in the MTGO client.

## License

This project is licensed under the [Apache-2.0 License](/LICENSE).

## Disclaimer

This project is protected under U.S., **Section 103(f)** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) protections for reverse-engineering for the purpose of enabling ‘interoperability’.

**Section 12.1(b)** of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) prohibits any modification, reverse engineering, or decompilation of the client '*except to the extent that such restriction is expressly prohibited by applicable law*'.

However, for such purposes protected under **Section 103(f)** of the DMCA, this EULA clause is statutorily preempted and rendered null and void; see also [*ML Genius Holdings LLC v. Google LLC, No. 20-3113* (2d Cir. Mar. 10, 2022)](https://casetext.com/case/ml-genius-holdings-llc-v-google-llc). All other provisions of the EULA remain in full force and effect unless otherwise prohibited by law.

**This is not legal advice.** Consult with a legal professional on your specific situation.
