/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

/// <summary>
/// Dump of a specific member (field, property) of a specific object
/// </summary>
public struct MemberDump
{
  public string Name { get; set; }
  public bool HasEncodedValue { get; set; }
  public string EncodedValue { get; set; }
  public string RetrievalError { get; set; }
}
