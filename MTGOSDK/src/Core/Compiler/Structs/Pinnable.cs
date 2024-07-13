/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Runtime.InteropServices;


namespace MTGOSDK.Core.Compiler.Structs;

/// <summary>
/// This class is used to make arbitrary objects "Pinnable" in the .NET
/// process's heap. Other objects are casted to it using "Unsafe.As" so their
/// first field's address overlaps with this class's <see cref="Data"/> field.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public sealed class Pinnable
{
  public byte Data;
}
