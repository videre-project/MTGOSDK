/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;
using System.Reflection;

using NUnit.Framework.Interfaces;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// Extend the implementation of <see cref="IReflectionInfo.GetCustomAttributes{T}"/>
/// by returning an extra attribute list. It resembles that the given method has
/// the extended attributes in its type.
/// </summary>
public sealed class CustomAttributeMethodWrapper(
  IMethodInfo baseInfo,
  Attribute[] extraAttributes) : IMethodInfo
{
  public T[] GetCustomAttributes<T>(bool inherit) where T : class =>
    baseInfo.GetCustomAttributes<T>(inherit)
      .Concat(extraAttributes.OfType<T>())
      .ToArray();

  public ITypeInfo TypeInfo => baseInfo.TypeInfo;

  public MethodInfo MethodInfo => baseInfo.MethodInfo;

  public string Name => baseInfo.Name;

  public bool IsAbstract => baseInfo.IsAbstract;

  public bool IsPublic => baseInfo.IsPublic;

  public bool IsStatic => baseInfo.IsStatic;

  public bool ContainsGenericParameters => baseInfo.ContainsGenericParameters;

  public bool IsGenericMethod => baseInfo.IsGenericMethod;

  public bool IsGenericMethodDefinition => baseInfo.IsGenericMethodDefinition;

  public ITypeInfo ReturnType => baseInfo.ReturnType;

  public Type[] GetGenericArguments() =>
    baseInfo.GetGenericArguments();

  public IParameterInfo[] GetParameters() =>
    baseInfo.GetParameters();

  public object Invoke(object? fixture, params object?[]? args) =>
    baseInfo.Invoke(fixture, args)!;

  public bool IsDefined<T>(bool inherit) where T : class =>
    baseInfo.IsDefined<T>(inherit);

  public IMethodInfo MakeGenericMethod(params Type[] typeArguments) =>
    baseInfo.MakeGenericMethod(typeArguments);
}
