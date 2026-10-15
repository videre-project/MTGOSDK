/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

[MessagePackObject(AllowPrivate = true)]
public class InvocationResults : IPinResponse
{
  [IgnoreMember]
  string? IPinResponse.PinOwner
  {
    set
    {
      if (ReturnedObjectOrAddress is not null)
        ReturnedObjectOrAddress.PinOwner = value;
    }
  }

  [Key(0)]
  public bool VoidReturnType { get; set; }
  [Key(1)]
  public ObjectOrRemoteAddress? ReturnedObjectOrAddress { get; set; }
}
