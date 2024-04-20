/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Interop.Exceptions;

internal class RemoteObjectMovedException(ulong testedAddress,
                                          string msg) : Exception(msg)
{
  public ulong TestedAddress { get; private set; } = testedAddress;
}
