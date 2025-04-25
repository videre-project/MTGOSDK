/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

//
// Hoist the NUnit.Framework and MTGOSDK.NUnit namespaces to the project level
// so that they can be used in all test files without needing to be imported.
//
global using NUnit.Framework;
global using MTGOSDK.NUnit.Attributes;

//
// Filter internal namespaces and members from error stacktraces thrown during
// unit tests. This is useful for filtering out runtime internals that MTGOSDK
// proxies and does not control.
//
[assembly: ExceptionFilter(
  @"^--",
  @"CallSite\.Target",
  @"InvokeStub_",
  @"System\.(Reflection|Dynamic|RuntimeMethodHandle|Threading\.ExecutionContext)",
  @"NUnit\.Framework",
  @"MTGOSDK\.NUnit",
  @"MTGOSDK\.Core\.(Remoting\.(Reflection|Types|Interop))",
  @"DLRWrapper\.Retry[T]"
)]
