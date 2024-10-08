/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;


namespace MTGOSDK.Core.Reflection.Types;

public class PropertyInfoStub : PropertyInfo
{
  public override string Name =>
    throw new NotImplementedException();

  public override Type DeclaringType =>
    throw new NotImplementedException();

  public override Type PropertyType =>
    throw new NotImplementedException();

  public override PropertyAttributes Attributes =>
    throw new NotImplementedException();

  public override Type ReflectedType => throw new NotImplementedException();

  public override MethodInfo GetMethod => throw new NotImplementedException();
  public override MethodInfo SetMethod => throw new NotImplementedException();

  public override bool CanRead => throw new NotImplementedException();
  public override bool CanWrite => throw new NotImplementedException();

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

  public override MethodInfo GetGetMethod(bool nonPublic)
  {
    throw new NotImplementedException();
  }

  public override MethodInfo GetSetMethod(bool nonPublic)
  {
    throw new NotImplementedException();
  }

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
    throw new NotImplementedException();
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
    throw new NotImplementedException();
  }

  public override string ToString() => throw new NotImplementedException();
}
