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
  private readonly AssemblyName _name = new(assemblyName);

  public override string FullName => _name.FullName;

  public override bool IsDynamic => true;

  public override AssemblyName GetName() => _name;
}
