/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting.Reflection;

/// <summary>
/// A parameter of a remote method.
/// </summary>
/// <remarks>
/// The parameter's type itself might also be a remote type but it can also
/// represent a local type.
/// </remarks>
public class RemoteParameterInfo(
  string name,
  LazyRemoteTypeResolver paramType) : ParameterInfoStub
{
  public override string Name { get; } = name;

  public override Type ParameterType => paramType.Value;

  // TODO: Type needs to be converted to a remote type ?
  public RemoteParameterInfo(ParameterInfo pi)
      : this(pi.Name, new LazyRemoteTypeResolver(pi.ParameterType))
  { }

  public override string ToString() => $"{paramType.TypeFullName} {Name}";
}
