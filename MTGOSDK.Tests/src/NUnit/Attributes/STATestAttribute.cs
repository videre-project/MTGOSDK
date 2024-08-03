/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;

using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Commands;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// This attribute forces an NUnit test to execute in an STA Thread.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class STATestAttribute : NUnitAttribute, IWrapTestMethod
{
  public virtual TestCommand GetSTACommand(TestCommand command) =>
    new STACommand(command);

  public TestCommand Wrap(TestCommand command)
  {
    return Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
      ? command
      : GetSTACommand(command);
  }
}
