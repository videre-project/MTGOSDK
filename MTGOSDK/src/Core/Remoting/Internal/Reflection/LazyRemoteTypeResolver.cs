/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Internal.Reflection;

public class LazyRemoteTypeResolver
{
  private Lazy<Type> _factory;
  private string _beforeDumpingTypeName;
  private string _beforeDumpingAssemblyName;
  private Type _resolved;

  public string Assembly => _resolved?.Assembly?.FullName ?? _beforeDumpingAssemblyName;
  public string TypeFullName => _resolved?.FullName ?? _beforeDumpingTypeName;

  public Type Value
  {
    get
    {
      _resolved ??= _factory.Value;
      return _resolved;
    }
  }

  public LazyRemoteTypeResolver(Lazy<Type> factory, string assembly, string typeFullName)
  {
    _factory = factory;
    _beforeDumpingAssemblyName = assembly;
    _beforeDumpingTypeName = typeFullName;
  }

  public LazyRemoteTypeResolver(Type resolved)
  {
    _resolved = resolved;
  }
}
