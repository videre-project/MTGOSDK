/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

// public readonly record struct DomainDump
// (
//   string Name,
//   IList<string> Modules
// );

public class DomainDump(string name, IList<string> modules)
{
  public string Name { get; set; } = name;
  public IList<string> Modules { get; set; } = modules;
}
