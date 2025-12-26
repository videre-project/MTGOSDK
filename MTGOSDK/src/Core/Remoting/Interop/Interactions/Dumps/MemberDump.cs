/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

/// <summary>
/// Dump of a specific member (field, property) of a specific object
/// </summary>
[MessagePackObject]
public struct MemberDump
{
  [Key(0)]
  public string Name { get; set; }
  [Key(1)]
  public string RetrievalError { get; set; }
}
