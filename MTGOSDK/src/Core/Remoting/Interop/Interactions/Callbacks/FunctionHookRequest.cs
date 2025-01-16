/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

public class FunctionHookRequest
{
  public string IP { get; set; }
  public int Port { get; set; }
  public string MethodName { get; set; }
  public string TypeFullName { get; set; }
  public List<string> ParametersTypeFullNames { get; set; }
  public string HookPosition { get; set; } // FFS: "Pre" or "Post"
}
