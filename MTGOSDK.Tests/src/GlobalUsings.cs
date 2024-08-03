/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

// Hoist the NUnit.Framework and MTGOSDK.NUnit namespaces to the project level
// so that they can be used in all test files without needing to be imported.
global using NUnit.Framework;
global using MTGOSDK.NUnit.Attributes;

// Apply a global retry policy to all tests defined in the assembly.
[assembly: RetryOnError(3)]
