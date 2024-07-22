/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection.Types;


namespace MTGOSDK.Core.Reflection;

public class TypeComparer : IEqualityComparer<Type>
{
  public bool Equals(Type x, Type y)
  {
    if (x is TypeStub || y is TypeStub)
      return true;
    return x.IsAssignableFrom(y);
  }

  public int GetHashCode(Type obj) => obj.GetHashCode();
}
