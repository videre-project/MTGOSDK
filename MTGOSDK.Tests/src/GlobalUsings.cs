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
// Apply a global retry policy to all tests defined in the assembly.
//
// By default, we opt to retry tests, leveraging the StackTrace filtering
// offered by this attribute's test runner to ensure consistent test reporting
// should this be used in a CI/CD pipeline under stricter failure conditions.
//
[assembly: RetryOnError(3, RetryBehavior.UntilPasses)]
[assembly: ExceptionFilter(
  @"^--",
  @"CallSite\.Target",
  @"InvokeStub_",
  @"System\.(Reflection|Dynamic|RuntimeMethodHandle|Threading\.ExecutionContext)",
  @"NUnit\.Framework",
  @"MTGOSDK\.NUnit",
  @"MTGOSDK\.Core\.(Remoting\.(Reflection|Types|Interop))"
)]
