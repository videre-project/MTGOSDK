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
        dynamic value = null;
        try
        {
          value = @base.GetType().GetProperty(binder.Name).GetValue(@base);

          // Get the default value for the return type.
          Type returnType = binder.ReturnType;
#if !MTGOSDKCORE
          dynamic typeRef = RuntimeHelpers.GetUninitializedObject(returnType);
#else // 'GetUninitializedObject' is not available in .NET Standard 2.0.
          dynamic typeRef = Activator.CreateInstance(returnType);
#endif
          dynamic typeDefault = typeRef
            .GetType()
            .GetConstructor(Type.EmptyTypes)
            .Invoke(typeRef, null);

#pragma warning disable CS8601
          result = (value != null || value != typeDefault)
            ? value
            : @default ?? value;
#pragma warning restore CS8601
        }
        catch
        {
          result = value ?? @default;
        }

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
