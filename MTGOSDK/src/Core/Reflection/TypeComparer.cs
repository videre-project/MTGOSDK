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
    // Check bidirectional assignability:
    // - x.IsAssignableFrom(y): y can be assigned to x (e.g., x=IEnumerable, y=List<T>)
    // - y.IsAssignableFrom(x): x can be assigned to y (e.g., x=List<T>, y=IEnumerable)
    // This allows method resolution to work when either the parameter or argument type is an interface.
    return x.IsAssignableFrom(y) || y.IsAssignableFrom(x);
  }

  public int GetHashCode(Type obj) => obj.GetHashCode();
}
