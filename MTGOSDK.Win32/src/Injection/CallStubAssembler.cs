/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Encodes CLR injection stubs for x86, x64, and ARM64.
/// </summary>
internal static class CallStubAssembler
{
  internal static void ValidateArchitecture(Architecture architecture)
  {
    if (architecture is not (Architecture.X86 or Architecture.X64 or Architecture.Arm64))
      throw new PlatformNotSupportedException(
        $"CLR injection is not supported for target architecture {architecture}.");
  }

  /// <summary>
  /// Encodes bootstrap instructions using addresses in target memory.
  /// </summary>
  public static byte[] CreateCallStub(ClrBootstrapArguments arguments, Architecture architecture)
  {
    if (arguments is null) throw new ArgumentNullException(nameof(arguments));
    ValidateArchitecture(architecture);
    return architecture == Architecture.Arm64
      ? Arm64CallStubAssembler.CreateCallStub(arguments)
      : IcedCallStubAssembler.CreateCallStub(arguments, architecture);
  }
}
