/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

[MessagePackObject]
public class InvocationResults
{
  [Key(0)]
  public bool VoidReturnType { get; set; }
  [Key(1)]
  public ObjectOrRemoteAddress? ReturnedObjectOrAddress { get; set; }
}
