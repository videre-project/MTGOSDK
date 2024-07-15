/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Structs;

/// <summary>
/// A candidate for a remote object.
/// </summary>
/// <remarks>
/// Holding this item does not mean having a meaningful hold of the remote object.
/// To gain one use <see cref="RemoteHandle"/>
/// </remarks>
public readonly record struct CandidateObject(
  ulong Address,
  string TypeFullName,
  int HashCode
);
