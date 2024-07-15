/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Reflection;

public class LazyRemoteTypeResolver(
  Lazy<Type> factory,
  string assembly,
  string typeFullName)
{
  public string Assembly => assembly;
  public string TypeFullName => typeFullName;
  public Type Value => factory.Value;

  public LazyRemoteTypeResolver(Type resolvedType)
      : this(new Lazy<Type>(() => resolvedType),
             resolvedType.Assembly.FullName,
             resolvedType.FullName)
  { }
}
