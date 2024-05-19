/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Marshaller for reading native data types from a remote process.
/// </summary>
public class CReader(IntPtr hProc, IntPtr hMod)
{
  /// <summary>
  /// Reads a sequence of bytes from the specified offset in memory.
  /// </summary>
  /// <param name="offset">The offset in the process memory where the read operation starts.</param>
  /// <param name="size">The number of bytes to read.</param>
  /// <returns>An array of bytes read from the process memory.</returns>
  public byte[] ReadBytes(int offset, int size)
  {
    var buffer = new byte[size];
    var hr = Kernel32.ReadProcessMemory(
      hProc,
      hMod + offset,
      buffer,
      (nuint)size,
      out var read
    );
    if (!hr)
      throw new Win32Exception(Kernel32.GetLastError());

    if (read != (nuint)size)
    {
      Debug.Assert(read < (nuint)size);
      Debug.Assert(read < int.MaxValue);
      Array.Resize(ref buffer, (int) read);
    }

    return buffer;
  }

  /// <summary>
  /// Reads a 32-bit signed integer from the specified offset in the data.
  /// </summary>
  /// <param name="offset">The offset at which to read the integer.</param>
  /// <returns>The 32-bit signed integer read from the data.</returns>
  public int ReadInt(int offset) =>
    BitConverter.ToInt32(ReadBytes(offset, 4), 0);

  /// <summary>
  /// Reads an array of elements from a specified offset in memory.
  /// </summary>
  /// <typeparam name="T">The type of elements in the array. Must be an unmanaged type.</typeparam>
  /// <param name="offset">The offset in memory to start reading from.</param>
  /// <param name="amount">The number of elements to read.</param>
  /// <returns>An array of elements read from memory.</returns>
  public T[] ReadArray<T>(uint offset, uint amount) where T : unmanaged
  {
    byte[] bytes = ReadBytes((int)offset, (int)(amount * Marshal.SizeOf<T>()));

    var arr = new T[amount];
    Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
    return arr;
  }

  /// <summary>
  /// Reads a structure of type T from the specified offset in memory.
  /// </summary>
  /// <typeparam name="T">The type of the structure to read. Must be an unmanaged type.</typeparam>
  /// <param name="offset">The offset in memory where the structure is located.</param>
  /// <returns>The structure read from memory.</returns>
  public T ReadStruct<T>(int offset) where T : unmanaged
  {
    byte[] bytes = ReadBytes(offset, Marshal.SizeOf<T>());

    var hStructure = Marshal.AllocHGlobal(bytes.Length);
    Marshal.Copy(bytes, 0, hStructure, bytes.Length);
    var structure = Marshal.PtrToStructure<T>(hStructure)!;
    Marshal.FreeHGlobal(hStructure);

    return structure;
  }

  /// <summary>
  /// Reads a null-terminated string from the specified offset in memory.
  /// </summary>
  /// <param name="offset">The offset in memory to start reading from.</param>
  /// <returns>The null-terminated string read from memory.</returns>
  public string ReadString(int offset)
  {
    byte b;
    var str = new StringBuilder();
    for (int i = 0; (b = ReadBytes(offset + i, 1)[0]) != 0; i++)
      str.Append((char)b);

    return str.ToString();
  }
}
