/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

public class IndexedItemAccessRequest
{
  public ulong CollectionAddress { get; set; }
  public bool PinRequest { get; set; }
  public ObjectOrRemoteAddress Index { get; set; }
}
