/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class DomainDump
{
  [Key(0)]
  public string Name { get; set; }
  [Key(1)]
  public IList<string> Modules { get; set; }

  public DomainDump() { }

  public DomainDump(string name, IList<string> modules)
  {
    Name = name;
    Modules = modules;
  }
}
