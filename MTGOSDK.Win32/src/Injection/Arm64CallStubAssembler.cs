/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using AsmArm64;
using static AsmArm64.Arm64Factory;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Encodes Windows ARM64 CLR hosting calls and their unwind record.
/// </summary>
internal static class Arm64CallStubAssembler
{
  private const int StackFrameSize = 16;
  private const int PointerSize = 8;
  private const int StartSlot = 3;
  private const int ExecuteInDefaultAppDomainSlot = 11;
  private const uint EFail = 0x80004005;
  private const uint MaxFunctionInstructions = 0x7ff;

  /// <summary>
  /// Encodes the bootstrap and an ARM64 RUNTIME_FUNCTION entry in one buffer.
  /// </summary>
  public static byte[] CreateCallStub(ClrBootstrapArguments arguments)
  {
    if (arguments.RegisterFunctionTable == IntPtr.Zero)
      throw new ArgumentException(
        "The ARM64 stub requires the target's RtlAddFunctionTable address.", nameof(arguments));

    using var assembler = new Arm64Assembler(0);
    var entry = assembler.CreateLabel("entry");
    var functionTable = assembler.CreateLabel("function_table");
    var failed = assembler.CreateLabel("failed");
    var epilogue = assembler.CreateLabel("epilogue");

    // Preserve FP/LR in a 16-byte frame for Windows ARM64 stack unwinding.
    // x18 is the target's TEB register and must remain untouched.
    assembler.Label(entry);
    assembler.STP(X29, X30, _[SP, -StackFrameSize].Pre);
    assembler.ADD(X29, SP, 0);

    // Relative labels keep the unwind table valid at any target allocation.
    // Register it before entering the CLR, which can walk this stack frame.
    assembler.ADR(X0, functionTable);
    assembler.MOVZ(W1, 1);
    assembler.ADR(X2, entry);
    LoadAddress(assembler, X16, arguments.RegisterFunctionTable);
    assembler.BLR(X16);
    assembler.CBZ(W0, failed);

    // CorBindToRuntimeEx(version, flavor, flags, CLSID, IID, &host).
    LoadAddress(assembler, X0, arguments.RuntimeVersion);
    LoadAddress(assembler, X1, arguments.BuildFlavor);
    assembler.MOVZ(W2, 0);
    LoadAddress(assembler, X3, arguments.ClassId);
    LoadAddress(assembler, X4, arguments.InterfaceId);
    LoadAddress(assembler, X5, arguments.RuntimeHost);
    LoadAddress(assembler, X16, arguments.BindToRuntime);
    assembler.BLR(X16);
    assembler.TBNZ(W0, 31, epilogue);

    // Reload the host between calls because Windows ARM64 argument registers
    // are volatile.
    LoadAddress(assembler, X0, arguments.RuntimeHost);
    assembler.LDR(X0, _[X0, 0]);
    assembler.CBZ(X0, failed);
    assembler.LDR(X16, _[X0, 0]);
    assembler.LDR(X16, _[X16, StartSlot * PointerSize]);
    assembler.BLR(X16);
    assembler.TBNZ(W0, 31, epilogue);

    // ICLRRuntimeHost::ExecuteInDefaultAppDomain is slot 11.
    LoadAddress(assembler, X0, arguments.RuntimeHost);
    assembler.LDR(X0, _[X0, 0]);
    LoadAddress(assembler, X1, arguments.AssemblyPath);
    LoadAddress(assembler, X2, arguments.TypeName);
    LoadAddress(assembler, X3, arguments.MethodName);
    LoadAddress(assembler, X4, arguments.Argument);
    LoadAddress(assembler, X5, arguments.ReturnValue);
    assembler.LDR(X16, _[X0, 0]);
    assembler.LDR(X16, _[X16, ExecuteInDefaultAppDomainSlot * PointerSize]);
    assembler.BLR(X16);
    assembler.B(epilogue);

    assembler.Label(failed);
    assembler.MOVZ(W0, (ushort)(EFail & 0xffff));
    assembler.MOVK(W0, (ushort)(EFail >> 16), default(IArm64ShiftKind.LSL), 16);
    assembler.Label(epilogue);
    assembler.LDP(X29, X30, _[SP], StackFrameSize);
    assembler.RET();

    uint functionLength = assembler.SizeInBytes;
    if (functionLength / 4 > MaxFunctionInstructions)
      throw new InvalidOperationException("ARM64 bootstrap exceeds the packed unwind function-length limit.");
    assembler.Align(8);
    assembler.Label(functionTable);
    assembler.Append(BitConverter.GetBytes(0u)); // BeginAddress, relative to entry.
    // Packed unwind: Flag=1, CR=3 (FP/LR chain), RegI=RegF=H=0.
    // Length is in instructions; frame size is in 16-byte units.
    // https://learn.microsoft.com/cpp/build/arm64-exception-handling#packed-unwind-data
    uint unwind = 1u | ((functionLength / 4) << 2) | (3u << 21) |
                  ((uint)(StackFrameSize / 16) << 23);
    assembler.Append(BitConverter.GetBytes(unwind));
    assembler.End();
    return assembler.Buffer.ToArray();
  }

  private static void LoadAddress(Arm64Assembler assembler, Arm64RegisterX register, IntPtr address)
  {
    ulong value = unchecked((ulong)address.ToInt64());
    assembler.MOVZ(register, (ushort)value);
    for (int shift = 16; shift < 64; shift += 16)
    {
      ushort part = (ushort)(value >> shift);
      if (part != 0) assembler.MOVK(register, part, default(IArm64ShiftKind.LSL), shift);
    }
  }
}
