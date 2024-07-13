/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

public class InvocationResults
{
  public bool VoidReturnType { get; set; }
  public ObjectOrRemoteAddress? ReturnedObjectOrAddress { get; set; }
}
