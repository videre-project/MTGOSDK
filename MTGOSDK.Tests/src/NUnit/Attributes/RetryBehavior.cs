/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

using MTGOSDK.NUnit.Extensions;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// Controls whether all tests should be retried and ensure that they all pass
/// on each retry, or that retries are exhausted until the test passes.
/// </summary>
public enum RetryBehavior
{
  /// <summary>
  /// Retry until the test passes, or the maximum number of retries is reached.
  /// </summary>
  UntilPasses = 0,

  /// <summary>
  /// Retry until the test fails, or the maximum number of retries is reached.
  /// </summary>
  UntilFails = 1,
}