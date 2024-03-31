/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Globalization;
using System.Reflection;


namespace MTGOSDK.Core.Remoting.Internal.Reflection;

public class RemotePropertyInfo(Type declaringType, Lazy<Type> propType, string name) : PropertyInfo
{
  private RemoteHandle App => (DeclaringType as RemoteType)?.App;
  public override PropertyAttributes Attributes =>
    throw new NotImplementedException();

  public override bool CanRead => GetMethod != null;
  public override bool CanWrite => SetMethod != null;

  public override Type PropertyType => propType.Value;

  public override Type DeclaringType { get; } = declaringType;

  public override string Name { get; } = name;

  public override Type ReflectedType =>
    throw new NotImplementedException();

  public RemoteMethodInfo RemoteGetMethod { get; set; }
  public RemoteMethodInfo RemoteSetMethod { get; set; }

  public override MethodInfo GetMethod => RemoteGetMethod;
  public override MethodInfo SetMethod => RemoteSetMethod;

  public RemotePropertyInfo(Type declaringType, Type propType, string name) :
    this(declaringType, new Lazy<Type>(() => propType), name)
  {}

  public RemotePropertyInfo(RemoteType declaringType, PropertyInfo pi)
      : this(declaringType, new Lazy<Type>(() => pi.PropertyType), pi.Name)
  {}

  public override MethodInfo[] GetAccessors(bool nonPublic)
  {
    throw new NotImplementedException();
  }

  public override object[] GetCustomAttributes(bool inherit)
  {
    throw new NotImplementedException();
  }

  public override object[] GetCustomAttributes(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override MethodInfo GetGetMethod(bool nonPublic) => this.GetMethod;
  public override MethodInfo GetSetMethod(bool nonPublic) => this.SetMethod;

  public override ParameterInfo[] GetIndexParameters()
  {
    throw new NotImplementedException();
  }

  public override object GetValue(
    object obj,
    BindingFlags invokeAttr,
    Binder binder,
    object[] index,
    CultureInfo culture)
  {
    RemoteMethodInfo getMethod = GetGetMethod() as RemoteMethodInfo;
    if (getMethod != null)
    {
      return getMethod.Invoke(obj, new object[0]);
    }
    else
    {
      throw new Exception($"Couldn't retrieve 'get' method of property '{this.Name}'");
    }
  }

  public override bool IsDefined(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override void SetValue(
    object obj,
    object value,
    BindingFlags invokeAttr,
    Binder binder,
    object[] index,
    CultureInfo culture)
  {
    RemoteMethodInfo setMethod = GetSetMethod() as RemoteMethodInfo;
    if (setMethod != null)
    {
      setMethod.Invoke(obj, new object[1] { value });
    }
    else
    {
      throw new Exception($"Couldn't retrieve 'set' method of property '{this.Name}'");
    }
  }

  public override string ToString() => $"{PropertyType.FullName} {Name}";
}
