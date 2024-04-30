<h1>
    <img
      align="top"
      src="/assets/icon.png"
      height="36"
      alt="MTGOSDK icon"
    />
  MTGOSDK
</h1>

<div align="center">

  <a href="/LICENSE">![License](https://img.shields.io/github/license/videre-project/mtgo-sdk?label=⚖%20License&labelColor=3f4551&color=9dc4d0)</a>
  <a href="https://dotnet.microsoft.com/en-us/download/dotnet">![.NET](https://img.shields.io/badge/dynamic/yaml?label=.NET&labelColor=3f4551&color=8a2be2&prefix=v&query=$.sdk.version&url=https://raw.githubusercontent.com/videre-project/mtgo-sdk/main/global.json)</a>
  <a href="https://www.mtgo.com">![MTGO](https://img.shields.io/badge/dynamic/xml.svg?logo=data:image/png;base64,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&labelColor=3f4551&color=da460e&label=MTGO&query=(//*[@name="MTGO.application"]/@version)[1]&url=http://mtgo.patch.Daybreakgames.com/patch/mtg/live/client/MTGO.application&maxAge=1800)</a>
  <a href="https://github.com/videre-project/mtgo-sdk/actions/workflows/build.yml">![Build](https://github.com/videre-project/mtgo-sdk/actions/workflows/build.yml/badge.svg)</a>

</div>

> [!WARNING]
> This project is still under construction and is not production-ready!

This SDK provides common APIs for accessing the **Magic: The Gathering Online (MTGO)** client's game state and player information, as well as internal states of the game engine useful for building tools that can assist with gameplay, such as deck trackers, or for analyzing game data for research purposes.

Refer to the project's [examples](/examples) for demo applications built with the SDK.

For more in-depth information on the SDK's APIs, refer to the project [documentation](/docs).

## Overview

This project consists of three main components:

* [**MTGOSDK**](MTGOSDK), a library providing high-level APIs for interacting with the MTGO client.
* [**MTGOSDK.MSBuild**](MTGOSDK.MSBuild), a MSBuild library for design/compile-time code generation of the SDK.
* [**MTGOSDK.Win32**](MTGOSDK.Win32), a library containing Win32 API definitions used by the SDK.

**MTGOSDK** — provides an API that exposes high-level abstractions of MTGO functions to read and manage the state of the client. This works by injecting the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) assembly into the MTGO process, bootstrapping a debugger interface to inspect objects from process memory. These objects are compiled by the runtime to enable reflection on heap objects as if they were live objects. This SDK is optimized for performance and ease-of-use without obstructing the player experience of the MTGO client.

**MTGOSDK.MSBuild** — an MSBuild library that manages the code-generation of the SDK. This is used to generate the SDK's API bindings and reference assemblies for the latest builds of MTGO. This builds a **MTGOSDK.Ref** reference assembly containing only the public types and members of internal classes from the MTGO client and does not handle or contain any implementation details or private code. As the MTGO client is updated, this assembly can be regenerated to provide the latest public types and members for use in the SDK. This library is not intended to be used directly by consumers of the SDK.

**MTGOSDK.Win32** — a library containing Win32 API definitions and helper functions used by the SDK. These are used to provide a more idiomatic C# API for Win32 functions and to ensure consistent API behavior across different versions of Windows. Additionally, this library serves as a reference for using Win32 APIs that are not yet implemented as part of the .NET Framework. This library is not intended to be used directly by consumers of the SDK.

## Building this Project

This project requires the [.NET Framework 4.8 SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48) and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to be installed with Visual Studio 2017 or newer. These can also be installed separately with the above installers or when installing Visual Studio with the [Visual Studio Installer](https://learn.microsoft.com/en-us/visualstudio/install/install-visual-studio?view=vs-2022).

Building against .NET Framework 4.8 also requires the [.NET Framework Developer Pack](https://aka.ms/msbuild/developerpacks) to be installed for building against reference assemblies. This is required for deterministic builds as the default lookup of .NET Framework SDKs through OS paths is disabled for msbuild.

Building this project with [MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild?view=vs-2022) (e.g. when using the msbuild or dotnet CLI) requires [Visual C++ Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/) to be installed. These redistributables are also optionally included with Visual Studio 2015 Update 3 and newer.

To build this solution, run either of the below commands from the root of the repository:

```powershell
# Build using the .NET CLI
$ dotnet build SDK.sln

# Build using MSBuild in Visual Studio
$ msbuild SDK.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

The SDK.sln solution will automatically build reference assemblies for the latest version of MTGO, even if no existing MTGO installation exists. This helps ensure that the SDK is always up-to-date with the latest versions of MTGO.

## Frequently Asked Questions

### What kind of applications can be built with this SDK?

This SDK can be used to build applications that interact cooperatively with the MTGO client, such as collection trackers, game analysis tools, or other assistive tools. This includes informational tools for streamers, integrations for importing/exporting decklists, etc.

Other projects like the [Videre Tracker](https://github.com/videre-project/Tracker) may allow extensions to be built that interact with it and the MTGO client by extending this SDK. This SDK should be preferred only in cases where an extension requires additional APIs not offered by the Tracker.

This SDK does not intend nor support creating cheating tools or hacks that violate the [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) of MTGO. Creation of such tools may result in account termination and other legal consequences, including prosecution by Daybreak Games or the Videre Project under applicable law.

### Does using this SDK modify the MTGO client?

No, this SDK does not modify or alter the MTGO client's code or start-up behavior.

Interactions with the MTGO process are managed through use of the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) library from Microsoft, which is injected into the MTGO process to inspect client memory through the use of snapshots. As this library does not support inspecting it's own process, it is isolated into a separate [application domain](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/application-domains) which acts as a process boundary. This allows for the SDK to read the state of the MTGO client without the ability to modify it's memory.

State changes are managed by [reflection](https://learn.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/reflection) on public APIs and events bound to MTGO's UI (i.e. viewmodels and controllers). This avoids hooking or executing code from MTGO's UI thread which might stall or break the state of the application. Instead, the underlying events bound to a UI element are used to propagate an interaction without locking the UI thread. These events enforce the same limits and safeguards set for a UI interaction, ensuring that all interactions are handled correctly.

### Is this SDK safe to use?

As this project uses the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) library to inspect the MTGO client's memory, only [user-mode COM debugging APIs](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/getting-started-with-windbg) are used to inspect the runtime and inform reflection. These APIs along with .NET's security model restrict access to sensitive objects in memory (such as user credentials, e.g. `SecureString` objects), ensuring that the SDK is only used for legitimate purposes even if compromised.

This is enforced through a tightly-integrated type marshalling protocol that only allows public interfaces to be exposed by MTGO. The use of these types (from **MTGOSDK.Ref**) eagerly validates and caches type metadata for all interactions (including reflection) managed by the .NET runtime, which cannot be unloaded or modified for the lifetime of the application. This ensures that faults in the SDK cannot bypass or affect internal states of the client.

Additionally, the high-level APIs provided by this SDK helps ensure safety and security in writing applications by providing simplified abstractions for interacting with the MTGO client. By only utilizing public interfaces and events, applications built on top of the SDK are less likely to break between updates or introduce bugs or security vulnerabilities. This SDK is designed to be easy to use and understand, and provides a consistent API for building all shapes and sizes of applications for MTGO.

### Is this SDK legal to use?

Yes, however there are important restrictions on how this SDK can be used with the MTGO client.

Daybreak Games reserves the right to terminate your account without notice or liability (at its sole discretion) upon breach of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula), the Daybreak Games [Terms of Service](https://www.daybreakgames.com/terms-of-service), or infringement of intellectual property rights (refer to **Section 16** of the EULA for more information on termination).

However, this restriction does not apply for purposes that do not violate the EULA and applicable law (such as copyright or intellectual property laws), or the aforementioned Daybreak Games policies. This includes such purposes protected under **Section 103** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) that do not otherwise infringe upon the rights granted to Daybreak Games (refer to the [Disclaimer](#disclaimer) for more information on protections afforded to this project).

**This is not legal advice.** Please consult with a legal professional for your specific situation.

## License

This project is licensed under the [Apache-2.0 License](/LICENSE).

## Disclaimer

> [!NOTE]
> This project is protected under U.S., **Section 103(f)** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) protections for reverse-engineering for the purpose of enabling ‘interoperability’.

**Section 12.1(b)** of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) prohibits any modification, reverse engineering, or decompilation of the client '*except to the extent that such restriction is expressly prohibited by applicable law*'.

However, for such purposes protected under **Section 103(f)** of the DMCA, this EULA clause is statutorily preempted by federal copyright law and rendered null and void; see also [*ML Genius Holdings LLC v. Google LLC, No. 20-3113* (2d Cir. Mar. 10, 2022)](https://casetext.com/case/ml-genius-holdings-llc-v-google-llc). All other provisions of the EULA remain in full force and effect unless otherwise prohibited by law.

Usage of this project for purposes prohibited by MTGO EULA and applicable law is not condoned by the project authors. The project authors are not responsible for any consequences of such usage.
