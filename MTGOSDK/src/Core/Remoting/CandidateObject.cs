/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting;

/// <summary>
/// A candidate for a remote object.
/// Holding this item does not mean having a meaningful hold of the remote object. To gain one use <see cref="RemoteHandle"/>
/// </summary>
public struct CandidateObject(ulong address, string typeFullName, int hashCode)
{
  public ulong Address = address;
  public string TypeFullName = typeFullName;
  public int HashCode = hashCode;
}
