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
  <a href="https://www.mtgo.com">![MTGO](https://img.shields.io/badge/dynamic/json.svg?logo=data:image/png;base64,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&labelColor=3f4551&color=da460e&label=MTGO&query=$.version&url=https://api.videreproject.com/mtgo/manifest&maxAge=1800)</a>
  <a href="https://github.com/videre-project/mtgo-sdk/actions/workflows/build.yml">![Build](https://github.com/videre-project/mtgo-sdk/actions/workflows/build.yml/badge.svg)</a>

</div>

## Overview

This SDK provides common APIs for accessing the **Magic: The Gathering Online (MTGO)** client's game state and player information, as well as internal states of the game engine useful for building tools that can assist with gameplay, such as deck trackers, or for analyzing game data for research purposes.

For example, to simply query the MTGO collection for a card, you can use the `CollectionManager` class to retrieve all printings of your favorite card:

```csharp
using MTGOSDK.API.Collection; // CollectionManager

IEnumerable<Card> printings = CollectionManager.GetCards("Colossal Dreadmaw");
foreach (Card card in printings)
{
  Console.WriteLine($"Name:      {card.Name}");          // "Colossal Dreadmaw"
  Console.WriteLine($"Colors:    {card.Colors}");        // "G"
  Console.WriteLine($"Mana Cost: {card.ManaCost}");      // "4GG"
  Console.WriteLine($"CMC:       {card.ConvertedCost}"); // 6
  string types = string.Join(", ", card.Types);
  Console.WriteLine($"Types:     {types)}");             // "Creature, Dinosaur"
  Console.WriteLine($"Power:     {card.Power}");         // 6
  Console.WriteLine($"Toughness: {card.Toughness}");     // 6
}
```

This will automatically connect to the MTGO client and retrieve these cards from the collection manager. The SDK will automatically connect and disconnect when needed, no setup required.

Or if you prefer, you can also explicitly manage the client connection yourself:

```csharp
using System;      // InvalidOperationException
using MTGOSDK.API; // Client

using (var client = new Client())
{
  if (!Client.IsLoggedIn)
    throw new InvalidOperationException("The MTGO client is not logged in.");

  string username = client.CurrentUser.Name;
  Console.WriteLine($"The current MTGO session is under '{username}'.");

  // Teardown when the MTGO client disconnects.
  client.IsConnectedChanged += delegate(object? sender)
  {
    if (!client.IsConnected)
    {
      Console.WriteLine("The MTGO client has been disconnected. Stopping...");
      client.Dispose(); // Manually dispose of our connection to the client.
      Environment.Exit(-1);
    }
  };

  // Do something with the client session.
}
// The connection to MTGO is automatically torn down when the using block exits.
```

Check out the [FAQ](/docs/FAQ.md) for common questions about the SDK, and the project's [examples](/examples) for demo applications built with the SDK.

## Documentation

Refer to the [SDK documentation](/docs/README.md) for more in-depth information about the SDK's APIs.

## Installation

> [!NOTE]
> Currently, the MTGOSDK is in early development and is not yet available on NuGet. You can currently build the SDK locally and reference it in your project using a local package feed.

The MTGOSDK is available as a NuGet package on the NuGet Gallery, and on GitHub Packages. You can install the package using the NuGet Package Manager in Visual Studio, or with the .NET Core CLI.

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

When building the project locally, you can use the SDK's local package feed to reference the development build. This feed is created by the SDK build process and is created under the `packages` directory.

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

To reference these packages, you can use the `*-dev*` version specifier in your project file:

```xml
<!-- Add to your project's .csproj or Directory.Packages.props file: -->
<PackageReference Include="MTGOSDK"
                  Version="*-dev*" />
<PackageReference Include="MTGOSDK.MSBuild"
                  Version="*-dev*"
                  PrivateAssets="All" />
<PackageReference Include="MTGOSDK.Win32"
                  Version="*-dev*" />
```

## Building this Project

This project requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to be installed with Visual Studio 2017 or newer. This can also be installed in Visual Studio using the [Visual Studio Installer](https://learn.microsoft.com/en-us/visualstudio/install/install-visual-studio?view=vs-2022).

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

MTGOSDK's remoting API is also based on an early version of [RemoteNET](https://github.com/theXappy/RemoteNET), which forms the backbone of the SDK's client-server architecture.

## License

This project is licensed under the [Apache-2.0 License](/LICENSE).

## Disclaimer

> [!NOTE]
> This project is protected under U.S., **Section 103(f)** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) protections for reverse-engineering for the purpose of enabling ‘interoperability’.

**Section 12.1(b)** of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) prohibits any modification, reverse engineering, or decompilation of the client '*except to the extent that such restriction is expressly prohibited by applicable law*'.

However, for such purposes protected under **Section 103(f)** of the DMCA, this EULA clause is statutorily preempted by federal copyright law and rendered null and void; see also [*ML Genius Holdings LLC v. Google LLC, No. 20-3113* (2d Cir. Mar. 10, 2022)](https://casetext.com/case/ml-genius-holdings-llc-v-google-llc). All other provisions of the EULA remain in full force and effect unless otherwise prohibited by law.

Usage of this project for purposes prohibited by MTGO EULA and applicable law is not condoned by the project authors. The project authors are not responsible for any consequences of such usage.
