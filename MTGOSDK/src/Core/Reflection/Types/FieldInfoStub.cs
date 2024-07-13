/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;


namespace MTGOSDK.Core.Reflection.Types;

public class FieldInfoStub : FieldInfo
{
  public override string Name => throw new NotImplementedException();
  public override Type FieldType => throw new NotImplementedException();
  public override Type DeclaringType => throw new NotImplementedException();
  public override Type ReflectedType => throw new NotImplementedException();
  public override FieldAttributes Attributes => throw new NotImplementedException();
  public override RuntimeFieldHandle FieldHandle => throw new NotImplementedException();

  public override object[] GetCustomAttributes(bool inherit)
  {
    throw new NotImplementedException();
  }

  public override object[] GetCustomAttributes(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override bool IsDefined(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override object GetValue(object obj)
  {
    throw new NotImplementedException();
  }

  public override void SetValue(
    object obj,
    object value,
    BindingFlags invokeAttr,
    Binder binder,
    CultureInfo culture)
  {
    throw new NotImplementedException();
  }

  public override string ToString() => throw new NotImplementedException();
}
