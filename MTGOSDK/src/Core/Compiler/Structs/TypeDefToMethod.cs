/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Compiler.Structs;

public struct TypeDefToMethod
{
  public ulong MethodTable { get; set; }
  public int Token { get; set; }
}
