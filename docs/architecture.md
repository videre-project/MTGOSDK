# Architecture

This document provides an overview of the architecture of the MTGOSDK and its components. It describes the high-level design of the SDK, its components, and how they interact with each other.

## Overview

This project consists of three main components:

* [**MTGOSDK**](MTGOSDK), a library providing high-level APIs for interacting with the MTGO client.
* [**MTGOSDK.MSBuild**](MTGOSDK.MSBuild), a MSBuild library for design/compile-time code generation of the SDK.
* [**MTGOSDK.Win32**](MTGOSDK.Win32), a library containing Win32 API definitions used by the SDK.

**MTGOSDK** — provides an API that exposes high-level abstractions of MTGO functions to read and manage the state of the client. This works by injecting the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) assembly into the MTGO process, bootstrapping a debugger interface to inspect objects from process memory. These objects are compiled by the runtime to enable reflection on heap objects as if they were live objects. This SDK is optimized for performance and ease-of-use without obstructing the player experience of the MTGO client.

**MTGOSDK.MSBuild** — an MSBuild library that manages the code-generation of the SDK. This is used to generate the SDK's API bindings and reference assemblies for the latest builds of MTGO. This builds a **MTGOSDK.Ref** reference assembly containing only the public types and members of internal classes from the MTGO client and does not handle or contain any implementation details or private code. As the MTGO client is updated, this assembly can be regenerated to provide the latest public types and members for use in the SDK. This library is not intended to be used directly by consumers of the SDK.

**MTGOSDK.Win32** — a library containing Win32 API definitions and helper functions used by the SDK. These are used to provide a more idiomatic C# API for Win32 functions and to ensure consistent API behavior across different versions of Windows. Additionally, this library serves as a reference for using Win32 APIs that are not yet implemented as part of the .NET Framework. This library is not intended to be used directly by consumers of the SDK.

> TODO: Add a diagram of the SDK architecture.

> TODO: Add a diagram of the SDK build process.

> TODO: Explain the MTGOSDK.MSBuild code-generation process.

> TODO: Explain the MTGOSDK DLR architecture and how it interacts with the MTGO client.
