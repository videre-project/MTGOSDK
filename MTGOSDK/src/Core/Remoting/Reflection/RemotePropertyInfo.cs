/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;

using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting.Reflection;

public class RemotePropertyInfo(
  Type declaringType,
  Lazy<Type> propType,
  string name) : PropertyInfoStub
{
  public override string Name { get; } = name;
  public override Type DeclaringType { get; } = declaringType;
  public override Type PropertyType => propType.Value;

  public RemoteMethodInfo RemoteGetMethod { get; set; }
  public RemoteMethodInfo RemoteSetMethod { get; set; }

  public override MethodInfo GetMethod => RemoteGetMethod;
  public override MethodInfo SetMethod => RemoteSetMethod;

  public override bool CanRead => GetMethod != null;
  public override bool CanWrite => SetMethod != null;

  public RemotePropertyInfo(Type declaringType, Type propType, string name) :
    this(declaringType, new Lazy<Type>(() => propType), name)
  { }

  public RemotePropertyInfo(RemoteType declaringType, PropertyInfo pi)
      : this(declaringType, new Lazy<Type>(() => pi.PropertyType), pi.Name)
  { }

  public override MethodInfo GetGetMethod(bool nonPublic) => this.GetMethod;
  public override MethodInfo GetSetMethod(bool nonPublic) => this.SetMethod;

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
