/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;


namespace MTGOSDK.Core.Reflection.Types;

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
