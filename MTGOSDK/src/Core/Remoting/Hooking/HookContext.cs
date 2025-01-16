/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.Core.Remoting.Hooking;


public class HookContext(string stackTrace)
{
  public string StackTrace => stackTrace;
  public bool CallOriginal => true;
}
