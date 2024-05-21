/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


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
