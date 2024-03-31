/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Collections.Generic;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

public class CallbackInvocationRequest
{
  public string StackTrace { get; set; }
  public int Token { get; set; }
  public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();
}
