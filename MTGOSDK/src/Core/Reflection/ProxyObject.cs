/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Dynamic;
using System.Runtime.CompilerServices;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Provides a dynamic object that can be used to wrap a static value.
/// </summary>
/// <param name="value">The value to wrap.</param>
public class ProxyObject(
  dynamic @base,
  dynamic @default = null,
  dynamic fallback = null): DynamicObject
{
  public override bool TryGetMember(GetMemberBinder binder, out object result)
  {
    Type returnType = binder.ReturnType;
    dynamic typeDefault = RuntimeHelpers.GetUninitializedObject(returnType);

    // First attempt to retrieve the member from the base object.
    try
    {
      try
      {
        if(!@base.TryGetMember(binder, out result))
        {
          result = null ?? @default;
          return false;
        }
      }
      // If the base object does not support dynamic binding, use reflection.
      catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
      {
        var value = @base.GetType().GetProperty(binder.Name).GetValue(@base);
        result = (value != null && value != typeDefault)
          ? value
          : @default ?? value;

        return true;
      }
    }
    // If the base object does not support dynamic binding, use fallback value.
    catch(NullReferenceException)
    {
      result = fallback ?? @default;
    }

    return true;
  }
}
