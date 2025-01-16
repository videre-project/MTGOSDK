/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Reflection;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

public class RegisteredMethodHookInfo
{
  /// <summary>
  /// The patch callback that was registered on the method
  /// </summary>
  public Delegate RegisteredProxy { get; set; }

  /// <summary>
  /// The method that was hooked
  /// </summary>
  public MethodBase OriginalHookedMethod { get; set; }

  /// <summary>
  /// The IP Endpoint listening for invocations
  /// </summary>
  public IPEndPoint Endpoint { get; set; }
}
