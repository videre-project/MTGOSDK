/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

[MessagePackObject]
public class FunctionHookRequest
{
  [Key(0)]
  public string IP { get; set; }
  [Key(1)]
  public int Port { get; set; }
  [Key(2)]
  public string MethodName { get; set; }
  [Key(3)]
  public string TypeFullName { get; set; }
  [Key(4)]
  public List<string> ParametersTypeFullNames { get; set; }
  [Key(5)]
  public string HookPosition { get; set; } // FFS: "Pre" or "Post"
}
