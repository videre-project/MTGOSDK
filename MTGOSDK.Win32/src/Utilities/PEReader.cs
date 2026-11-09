/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.Utilities;

/// <summary>
/// Utility class for reading information from a PE file.
/// </summary>
public static class PEReader
{
  /// <summary>
  /// Represents the IMAGE_EXPORT_DIRECTORY structure in a PE file.
  /// </summary>
  private struct ImageExportDirectory
  {
#pragma warning disable CS0649
    public uint Characteristics;
    public uint TimeDateStamp;
    public ushort MajorVersion;
    public ushort MinorVersion;

    public uint Name;
    public uint Base;
    public uint NumberOfFunctions;
    public uint NumberOfNames;
    public uint AddressOfFunctions;
    public uint AddressOfNames;
    public uint AddressOfNameOrdinals;
#pragma warning restore CS0649
  }

  /// <summary>
  /// Reads a PE header to identify native module architecture.
  /// </summary>
  /// <exception cref="InvalidDataException">The DOS or PE header is incomplete or invalid.</exception>
  /// <exception cref="PlatformNotSupportedException">The machine type is unsupported.</exception>
  internal static Architecture GetArchitecture(IntPtr hProc, IntPtr hMod)
  {
    var reader = new CReader(hProc, hMod);
    byte[] dos = reader.ReadBytes(0, 0x40);
    if (dos.Length != 0x40 || BitConverter.ToUInt16(dos, 0) != 0x5A4D)
      throw new InvalidDataException("The target module has an incomplete or invalid DOS header.");

    int offset = BitConverter.ToInt32(dos, 0x3C);
    if (offset < 0x40 || offset > int.MaxValue - 6)
      throw new InvalidDataException("The target module has an invalid PE header offset.");

    byte[] header = reader.ReadBytes(offset, 6);
    if (header.Length != 6 || BitConverter.ToUInt32(header, 0) != 0x00004550)
      throw new InvalidDataException("The target module has an incomplete or invalid PE header.");

    ushort machine = BitConverter.ToUInt16(header, 4);
    return machine switch
    {
      0x014C => Architecture.X86,
      0x8664 => Architecture.X64,
      0xAA64 => Architecture.Arm64,
      _ => throw new PlatformNotSupportedException($"Unsupported target PE machine: 0x{machine:X4}.")
    };
  }

  /// <summary>
  /// Retrieves the address of an exported function from a module.
  /// </summary>
  /// <param name="hProc">The handle to the process containing the module.</param>
  /// <param name="hMod">The handle to the module containing the function.</param>
  /// <param name="name">The name of the function to retrieve.</param>
  /// <param name="x86">Whether the module is 32-bit or 64-bit.</param>
  /// <returns>The address of the exported function.</returns>
  /// <exception cref="KeyNotFoundException">The function could not be found.</exception>
  public static int GetExportAddress(
    IntPtr hProc,
    IntPtr hMod,
    string name,
    bool x86 = false)
  {
    var dic = GetAllExportAddresses(hProc, hMod, x86);

    if (!dic.ContainsKey(name))
      throw new KeyNotFoundException(
          $"Could not find function with name {name}.");

    return dic[name];
  }

  /// <summary>
  /// Retrieves all exported functions from a module.
  /// </summary>
  /// <param name="hProc">The handle to the process containing the module.</param>
  /// <param name="hMod">The handle to the module containing the functions.</param>
  /// <param name="x86">Whether the module is 32-bit or 64-bit.</param>
  /// <returns>A dictionary mapping function names to their addresses.</returns>
  private static Dictionary<string, int> GetAllExportAddresses(
    IntPtr hProc,
    IntPtr hMod,
    bool x86 = false)
  {
    var c = new CReader(hProc, hMod);
    var dic = new Dictionary<string, int>();

    int hdr = c.ReadInt(0x3C); // PE signature offset from DOS header
    int exportTableRva = c.ReadInt(hdr + (x86 ? 0x78 : 0x88));
    var exportTable = c.ReadStruct<ImageExportDirectory>(exportTableRva);

    // Retrieve all exported functions with their name and ordinal.
    int[] functions = c.ReadArray<int>(
      exportTable.AddressOfFunctions,
      exportTable.NumberOfFunctions);
    int[] names = c.ReadArray<int>(
      exportTable.AddressOfNames,
      exportTable.NumberOfNames);
    ushort[] ordinals = c.ReadArray<ushort>(
      exportTable.AddressOfNameOrdinals,
      exportTable.NumberOfFunctions);

    // Create a dictionary mapping names to their respective functions.
    for (int i = 0; i < names.Length; i++)
      if (names[i] != 0)
        dic[c.ReadString(names[i])] = functions[ordinals[i]];

    return dic;
  }
}
