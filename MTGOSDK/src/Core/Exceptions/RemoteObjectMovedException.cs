/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

public class RemoteObjectMovedException(ulong testedAddress, string msg)
    : Exception(msg)
{
  public ulong TestedAddress { get; private set; } = testedAddress;
}
