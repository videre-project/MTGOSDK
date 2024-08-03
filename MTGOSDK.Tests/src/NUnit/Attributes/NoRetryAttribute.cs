/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// Specifies that the test assembly, fixture, or test method should not have
/// retry functionality applied.
/// </summary>
/// <remarks>
/// This attribute respects hierarchy like <see cref="RetryOnErrorAttribute" />
/// but ignores retry in fixture or method levels if already applied at higher
/// levels, such as the assembly.
/// </remarks>
[AttributeUsage(AttributeTargets.Class |
                AttributeTargets.Method, AllowMultiple=false, Inherited=true)]
public class NoRetryAttribute : NUnitAttribute
{ }
