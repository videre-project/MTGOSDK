/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;


namespace RemoteNET.Internal.Reflection;

public class RemoteMethodInfo(
  Type declaringType,
  LazyRemoteTypeResolver returnType,
  string name,
  Type[] genericArgs,
  ParameterInfo[] paramInfos) : MethodInfo
{
  public override ICustomAttributeProvider ReturnTypeCustomAttributes =>
    throw new NotImplementedException();

  public override string Name { get; } = name;

  public override Type DeclaringType { get; } = declaringType;

  public override Type ReturnType => returnType.Value;

  public override Type ReflectedType =>
    throw new NotImplementedException();

  public override RuntimeMethodHandle MethodHandle =>
    throw new NotImplementedException();

  public override MethodAttributes Attributes =>
    throw new NotImplementedException();

  public override bool IsGenericMethod =>
    AssignedGenericArgs.Length > 0;

  public override bool IsGenericMethodDefinition =>
    AssignedGenericArgs.Length > 0 &&
    AssignedGenericArgs.All(t => t is DummyGenericType);

  public override bool ContainsGenericParameters =>
    AssignedGenericArgs.Length > 0 &&
    AssignedGenericArgs.All(t => t is DummyGenericType);

  public override Type[] GetGenericArguments() =>
    AssignedGenericArgs;

  public Type[] AssignedGenericArgs { get; } = genericArgs ?? Type.EmptyTypes;

  private RemoteApp App => (DeclaringType as RemoteType)?.App;

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

  public override object[] GetCustomAttributes(bool inherit)
  {
    throw new NotImplementedException();
  }

  public override bool IsDefined(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override ParameterInfo[] GetParameters() => paramInfos;

  public override MethodImplAttributes GetMethodImplementationFlags()
  {
    throw new NotImplementedException();
  }

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

  public override MethodInfo GetBaseDefinition()
  {
    throw new NotImplementedException();
  }

  public override object[] GetCustomAttributes(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
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
