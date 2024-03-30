/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Reflection;


namespace RemoteNET.Internal.Reflection;

public class RemoteEventInfo(
  RemoteType declaringType,
  Lazy<Type> eventHandlerType,
  string name) : EventInfo
{
  public override EventAttributes Attributes =>
    throw new NotImplementedException();

  public override Type DeclaringType { get; } = declaringType;

  public override string Name { get; } = name;

  public override Type ReflectedType =>
    throw new NotImplementedException();

  public RemoteMethodInfo RemoteAddMethod { get; set; }
  public RemoteMethodInfo RemoteRemoveMethod { get; set; }
  public override MethodInfo AddMethod => RemoteAddMethod;
  public override MethodInfo RemoveMethod => RemoteRemoveMethod;

  public override Type EventHandlerType => eventHandlerType.Value;

  public RemoteEventInfo(
    RemoteType declaringType,
    Type eventHandlerType,
    string name)
      : this(declaringType, new Lazy<Type>(() => eventHandlerType), name)
  { }

  public RemoteEventInfo(
    RemoteType declaringType,
    EventInfo ei)
      : this(declaringType, new Lazy<Type>(() => ei.EventHandlerType), ei.Name)
  { }

  public override MethodInfo GetAddMethod(bool nonPublic) => RemoteAddMethod;
  public override MethodInfo GetRemoveMethod(bool nonPublic) => RemoteRemoveMethod;

  public override object[] GetCustomAttributes(bool inherit)
  {
    throw new NotImplementedException();
  }

  public override object[] GetCustomAttributes(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override MethodInfo GetRaiseMethod(bool nonPublic)
  {
    throw new NotImplementedException();
  }

  public override bool IsDefined(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override string ToString() => $"{eventHandlerType.Value} {Name}";
}
