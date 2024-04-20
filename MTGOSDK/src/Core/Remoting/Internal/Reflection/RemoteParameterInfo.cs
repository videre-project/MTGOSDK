/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Reflection;


namespace MTGOSDK.Core.Remoting.Internal.Reflection;

/// <summary>
/// A parameter of a remote method. The parameter's type itself might be a remote type (but can also be local)
/// </summary>
public class RemoteParameterInfo(
  string name,
  LazyRemoteTypeResolver paramType) : ParameterInfo
{
  public override string Name { get; } = name;

  public override Type ParameterType => paramType.Value;

  // TODO: Type needs to be converted to a remote type ?
  public RemoteParameterInfo(ParameterInfo pi)
      : this(pi.Name, new LazyRemoteTypeResolver(pi.ParameterType))
  { }

  public override string ToString() => $"{paramType.TypeFullName} {Name}";
}
