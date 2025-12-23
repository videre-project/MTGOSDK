/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class TypesDump
{
  [MessagePackObject]
  public struct TypeIdentifiers
  {
    [Key(0)]
    public string TypeName { get; set; }
  }

  [Key(0)]
  public string AssemblyName { get; set; }
  [Key(1)]
  public List<TypeIdentifiers> Types { get; set; }
}
