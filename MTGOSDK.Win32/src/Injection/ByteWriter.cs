/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using Iced.Intel;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Wrapper for encoding a sequence of instructions into a byte array.
/// </summary>
internal sealed class ByteWriter : CodeWriter
{
  private readonly List<byte> allBytes = new List<byte>();
  public override void WriteByte(byte value) => allBytes.Add(value);
  public byte[] ToArray() => allBytes.ToArray();
}
