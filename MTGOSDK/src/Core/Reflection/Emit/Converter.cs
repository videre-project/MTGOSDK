/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Reflection.Emit;

using MTGOSDK.Core.Remoting.Interop.Extensions;


namespace MTGOSDK.Core.Reflection.Emit;

/// <summary>
/// A class that converts an IntPtr to an object reference.
/// </summary>
public class Converter<T>
{
  /// <summary>
  /// The delegate that converts an IntPtr to an object reference.
  /// </summary>
  delegate U Void2ObjectConverter<U>(IntPtr pManagedObject);

  /// <summary>
  /// The converter instance that converts an IntPtr to an object reference.
  /// </summary>
  private static Void2ObjectConverter<T> myConverter;

  static Converter()
  {
    //
    // The type initializer is run every time the converter is instantiated
    // using a different generic argument.
    //
    GenerateDynamicMethod();
  }

  /// <summary>
  /// Generates a dynamic method that converts an IntPtr to an object reference.
  /// </summary>
  /// <remarks>
  /// The dynamic method trick is discussed originally by Alois Kraus here:
  /// https://social.microsoft.com/Forums/windows/en-US/06ac44b0-30d8-44a1-86a4-1716dc431c62/how-to-convert-an-intptr-to-an-object-in-c?forum=clr
  /// </remarks>
  static void GenerateDynamicMethod()
  {
    if (myConverter == null)
    {
      DynamicMethod method = new("ConvertPtrToObjReference",
          typeof(T),
          new Type[] { typeof(IntPtr) },
          typeof(IntPtr),
          true);
      var gen = method.GetILGenerator();
      // Load first argument
      gen.Emit(OpCodes.Ldarg_0);
      // Return it directly. The Clr will take care of the cast!
      // This construct is unverifiable so we need to plug this into an assembly
      // with IL Verification disabled.
      gen.Emit(OpCodes.Ret);
      myConverter = (Void2ObjectConverter<T>)method
        .CreateDelegate(typeof(Void2ObjectConverter<T>));
    }
  }

  /// <summary>
  /// Handles the conversion of an IntPtr to an object reference.
  /// </summary>
  /// <param name="pObj">The IntPtr to convert.</param>
  /// <param name="expectedMethodTable">The expected method table of the object.</param>
  /// <returns>The object reference.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown when the actual method table value is not as expected.
  /// </exception>
  /// <remarks>
  /// This methods reads the method table of the object to make sure we aren't
  /// mistakenly pointing at another type by now (could be caused by the GC).
  /// </remarks>
  public T ConvertFromIntPtr(IntPtr pObj, IntPtr expectedMethodTable)
  {
    IntPtr actualMethodTable = pObj.GetMethodTable();
    if (actualMethodTable != expectedMethodTable)
    {
      throw new ArgumentException("Actual Method Table value was not as expected");
    }
    return myConverter(pObj);
  }

  /// <summary>
  /// Handles the conversion of an IntPtr to an object reference.
  /// </summary>
  /// <param name="pObj">The IntPtr to convert.</param>
  /// <param name="expectedMethodTable">The expected method table of the object.</param>
  /// <returns>The object reference.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown when the actual method table value is not as expected.
  /// </exception>
  /// <remarks>
  /// This methods reads the method table of the object to make sure we aren't
  /// mistakenly pointing at another type by now (could be caused by the GC).
  /// </remarks>
  public T ConvertFromIntPtr(ulong pObj, ulong expectedMethodTable) =>
    ConvertFromIntPtr(
      new IntPtr((long) pObj),
      new IntPtr((long) expectedMethodTable));
}
