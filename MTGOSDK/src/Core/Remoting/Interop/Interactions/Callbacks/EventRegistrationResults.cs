/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

[MessagePackObject]
public class EventRegistrationResults
{
  [Key(0)]
  public int Token { get; set; }
}
