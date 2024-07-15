/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;

using MTGOSDK.Core.Reflection.Types;


namespace MTGOSDK.Core.Remoting.Types;

public class RemoteAssembly(string assemblyName) : Assembly
{
  private AssemblyName name = new(assemblyName);

  public override string FullName =>
    throw new Exception(
      $"You tried to get the 'FullName' property on a {nameof(RemoteAssembly)}." +
      $"Currently, this is forbidden to reduce confusion between 'full name' and 'short name'." +
      $"You should call 'GetName().Name' instead.");

  public override AssemblyName GetName() => name;
}
