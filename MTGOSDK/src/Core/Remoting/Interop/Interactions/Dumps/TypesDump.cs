﻿/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

public class TypesDump
{
  public struct TypeIdentifiers
  {
    public string TypeName { get; set; }
  }

  public string AssemblyName { get; set; }
  public List<TypeIdentifiers> Types { get; set; }
}
