/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Hooking;

public enum HarmonyPatchPosition
{
  //All,
  Prefix,
  Postfix,
  //Transpiler,
  Finalizer,
  //ReversePatch
}
