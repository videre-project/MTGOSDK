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

public class RemoteConstructorInfo(
  Type declaringType,
  ParameterInfo[] paramInfos) : ConstructorInfoStub
{
  public override string Name => ".ctor";
  public override Type DeclaringType => declaringType;

  private RemoteHandle App => (declaringType as RemoteType)?.App;

  public RemoteConstructorInfo(RemoteType declaringType, ConstructorInfo ci) :
    this(declaringType,
        ci.GetParameters()
          .Select(pi => new RemoteParameterInfo(pi))
          .Cast<ParameterInfo>()
          .ToArray())
  { }

  public override ParameterInfo[] GetParameters() => paramInfos;

  public override object Invoke(
    BindingFlags invokeAttr,
    Binder binder,
    object[] parameters,
    CultureInfo culture)
  {
    return RemoteFunctionsInvokeHelper
      .Invoke(App, DeclaringType, Name, null, new Type[0], parameters);
  }

  public override object Invoke(
    object obj,
    BindingFlags invokeAttr,
    Binder binder,
    object[] parameters,
    CultureInfo culture)
  {
    // Empirically, invoking a ctor on an existing object should return null.
    if (obj == null)
    {
      // Last chance - If this overload was used but no real object given lets
      // redirect to normal Invoke (also happens with normal 'ConstructorInfo's)
      return Invoke(invokeAttr, binder, parameters, culture);
    }

    return null;
  }

  public override string ToString()
  {
    string args = string.Join(", ", paramInfos.Select(pi => pi.ParameterType.FullName));
    return $"Void {this.Name}({args})";
  }
}
