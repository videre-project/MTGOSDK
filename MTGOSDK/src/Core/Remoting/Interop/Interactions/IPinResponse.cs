/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.Core.Remoting.Interop.Interactions;

/// <summary>
/// Receives the registration that acquired the response's pin references.
/// This metadata is carried by the enclosing response, not by individual values.
/// </summary>
internal interface IPinResponse
{
  string? PinOwner { set; }
}
