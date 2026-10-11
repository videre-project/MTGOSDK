/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Client;

[MessagePackObject]
public struct UnregisterClientResponse
{
  [Key(0)]
  public bool WasRemoved { get; set; }

  /// <summary>
  /// Number of remaining clients, after the removal was done
  /// </summary>
  [Key(1)]
  public int OtherClientsAmount { get; set; }
}
