/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Client;

public struct UnregisterClientResponse
{
  public bool WasRemoved { get; set; }

  /// <summary>
  /// Number of remaining clients, after the removal was done
  /// </summary>
  public int OtherClientsAmount { get; set; }
}
