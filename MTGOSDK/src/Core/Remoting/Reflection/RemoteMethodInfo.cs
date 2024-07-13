/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;

using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting.Reflection;

public class RemoteMethodInfo(
  Type declaringType,
  LazyRemoteTypeResolver returnType,
  string name,
  Type[] genericArgs,
  ParameterInfo[] paramInfos) : MethodInfoStub
{
  public override string Name { get; } = name;

  public override Type DeclaringType { get; } = declaringType;

  public override Type ReturnType => returnType.Value;

  public override bool IsGenericMethod =>
    AssignedGenericArgs.Length > 0;

  public override bool IsGenericMethodDefinition =>
    AssignedGenericArgs.Length > 0 &&
    AssignedGenericArgs.All(t => t is TypeStub);

  public override bool ContainsGenericParameters =>
    AssignedGenericArgs.Length > 0 &&
    AssignedGenericArgs.All(t => t is TypeStub);

  public override Type[] GetGenericArguments() =>
    AssignedGenericArgs;

  public Type[] AssignedGenericArgs { get; } = genericArgs ?? Type.EmptyTypes;

  private RemoteHandle App => (DeclaringType as RemoteType)?.App;

  public RemoteMethodInfo(RemoteType declaringType, MethodInfo mi) :
    this(declaringType,
      new LazyRemoteTypeResolver(mi.ReturnType),
      mi.Name,
      mi.GetGenericArguments(),
      mi.GetParameters()
        .Select(pi => new RemoteParameterInfo(pi))
        .Cast<ParameterInfo>()
        .ToArray())
  {}

  public RemoteMethodInfo(
    Type declaringType,
    Type returnType,
    string name,
    Type[] genericArgs,
    ParameterInfo[] paramInfos) : this(declaringType, new LazyRemoteTypeResolver(returnType), name, genericArgs, paramInfos)
  { }

  public override MethodInfo MakeGenericMethod(params Type[] typeArguments)
  {
    return new RemoteMethodInfo(DeclaringType, ReturnType, Name, typeArguments, paramInfos);
  }

  public override ParameterInfo[] GetParameters() => paramInfos;

  public override object Invoke(
    object obj,
    BindingFlags invokeAttr,
    Binder binder,
    object[] parameters,
    CultureInfo culture)
  {
    return RemoteFunctionsInvokeHelper
      .Invoke(this.App, DeclaringType, Name, obj, AssignedGenericArgs, parameters);
  }
  public override string ToString()
  {
    try
    {
      string args = string.Join(", ", paramInfos.Select(pi => pi.ToString()));
      return $"{returnType.TypeFullName} {Name}({args})";
    }
    catch (Exception)
    {
      throw;
    }
  }
}
