/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Encodes x86 and x64 CLR hosting calls.
/// </summary>
internal static class IcedCallStubAssembler
{
  /// <summary>
  /// Adds a call stub to the assembler code.
  /// </summary>
  /// <param name="c">The assembler instance.</param>
  /// <param name="regAddr">The address of the function to call.</param>
  /// <param name="arguments">The arguments in call order.</param>
  /// <param name="x86">Whether to encode x86 instructions.</param>
  /// <param name="cleanStack">Whether to remove x86 stack arguments after the call.</param>
  private static void AddCallStub(
    Assembler c,
    IntPtr regAddr,
    object[] arguments,
    bool x86,
    bool cleanStack = false)
  {
    if (x86)
    {
      c.mov(eax, regAddr.ToInt32());
      AddCallStub(c, eax, arguments, true, cleanStack);
    }
    else
    {
      c.mov(rax, regAddr.ToInt64());
      AddCallStub(c, rax, arguments, false, cleanStack);
    }
  }

  /// <summary>
  /// Adds a call stub to the assembler code.
  /// </summary>
  /// <param name="c">The assembler instance.</param>
  /// <param name="regFun">The register containing the function address.</param>
  /// <param name="arguments">The arguments to pass to the function.</param>
  /// <param name="x86">Whether to encode x86 instructions.</param>
  /// <param name="cleanStack">Whether to remove x86 stack arguments after the call.</param>
  private static void AddCallStub(
    Assembler c,
    Register regFun,
    object[] arguments,
    bool x86,
    bool cleanStack = false)
  {
    if (x86)
    {
      for (int i = arguments.Length - 1; i >= 0; i--)
      {
        switch (arguments[i])
        {
          case IntPtr p:
            c.push(p.ToInt32());
            break;
          case int i32:
            c.push(i32);
            break;
          case byte u8:
            c.push(u8);
            break;
          case AssemblerRegister32 reg:
            c.push(reg);
            break;
          case AssemblerRegister64 reg:
            c.push(reg);
            break;
          default:
            throw new NotSupportedException(
                $"Unsupported parameter type {arguments[i].GetType()} on x86");
        }
      }

      c.call(new AssemblerRegister32(regFun));

      if (cleanStack && arguments.Length > 0)
        c.add(esp, arguments.Length * IntPtr.Size);
    }
    else
    {
      // calling convention: https://docs.microsoft.com/en-us/cpp/build/x64-calling-convention?view=vs-2019
      var tempReg = rax;

      // Preserve RAX while staging arguments; it may hold the function address.
      c.push(tempReg);

      for (int i = arguments.Length - 1; i >= 0; i--) {
        var arg = arguments[i];
        var argReg = i switch { 0 => rcx, 1 => rdx, 2 => r8, 3 => r9, _ => default };
        if (i > 3)
        {
          // Stack argument offsets include the saved RAX value.
          if (arg is AssemblerRegister64 r)
          {
            c.mov(__[rsp + 0x20 + (i - 3) * 8], r);
          }
          else
          {
            c.mov(tempReg, convertToLong(arg));
            c.mov(__[rsp + 0x20 + (i - 3) * 8], tempReg);
          }
        }
        else
        {
          if (arg is AssemblerRegister64 r)
          {
            c.mov(argReg, r);
          }
          else
          {
            c.mov(argReg, convertToLong(arg));
          }
        }

        long convertToLong(object o) =>
          o switch
          {
            IntPtr p => p.ToInt64(),
            UIntPtr p => (long)p.ToUInt64(),
            _ => Convert.ToInt64(o),
          };
      }

      c.pop(tempReg);

      c.call(new AssemblerRegister64(regFun));
    }
  }

  /// <summary>
  /// Encodes the x86 or x64 bootstrap using target-process addresses.
  /// </summary>
  public static byte[] CreateCallStub(ClrBootstrapArguments arguments, Architecture architecture)
  {
    bool x86 = architecture == Architecture.X86;
    var fnAddr = arguments.BindToRuntime;
    var pwszVersion = arguments.RuntimeVersion;
    var pwszBuildFlavor = arguments.BuildFlavor;
    var rcslid = arguments.ClassId;
    var riid = arguments.InterfaceId;
    var ppv = arguments.RuntimeHost;
    var pwzAssemblyPath = arguments.AssemblyPath;
    var pwzTypeName = arguments.TypeName;
    var pwzMethodName = arguments.MethodName;
    var pwzArgument = arguments.Argument;
    var pReturnValue = arguments.ReturnValue;

    var c = new Assembler(x86 ? 32 : 64);

    void AddCallReg(Register r, params object[] callArgs) =>
      AddCallStub(c, r, callArgs, x86);
    void AddCallPtr(IntPtr fn, params object[] callArgs) =>
      AddCallStub(c, fn, callArgs, x86);

    if (x86)
    {
      // call CorBindToRuntimeEx
      AddCallPtr(fnAddr, pwszVersion, pwszBuildFlavor, (byte)0, rcslid, riid, ppv);

      // call ICLRRuntimeHost::Start
      c.mov(edx, __[ppv.ToInt32()]);
      c.mov(eax, __[edx]);
      c.mov(eax, __[eax + 0x0C]);
      AddCallReg(eax, edx);

      // call ICLRRuntimeHost::ExecuteInDefaultAppDomain
      c.mov(edx, __[ppv.ToInt32()]);
      c.mov(eax, __[edx]);
      c.mov(eax, __[eax + 0x2C]);
      AddCallReg(eax, edx, pwzAssemblyPath, pwzTypeName, pwzMethodName, pwzArgument, pReturnValue);

      c.ret();
    }
    else
    {
      // reserve stack space for arguments
      const int maxStackIndex = 3;
      const int stackOffset = 0x20;
      c.sub(rsp, stackOffset + maxStackIndex * 8);

      // call CorBindToRuntimeEx
      AddCallPtr(fnAddr, pwszVersion, pwszBuildFlavor, 0, rcslid, riid, ppv);

      // call pClrHost->Start();
      c.mov(rcx, ppv.ToInt64());
      c.mov(rcx, __[rcx]);
      c.mov(rax, __[rcx]);
      c.mov(rdx, __[rax + 0x18]);
      AddCallReg(rdx, rcx);

      // call pClrHost->ExecuteInDefaultAppDomain()
      c.mov(rcx, ppv.ToInt64());
      c.mov(rcx, __[rcx]);
      c.mov(rax, __[rcx]);
      c.mov(rax, __[rax + 0x58]);
      AddCallReg(rax, rcx, pwzAssemblyPath, pwzTypeName, pwzMethodName, pwzArgument, pReturnValue);

      // restore stack
      c.add(rsp, stackOffset + maxStackIndex * 8);

      c.ret();
    }

    var writer = new ByteWriter();
    var block = new InstructionBlock(writer, new List<Instruction>(c.Instructions), 0);
    if (!BlockEncoder.TryEncode(x86 ? 32 : 64, block, out string? error, out _))
      throw new InvalidOperationException("Encountered an error during Iced encode: " + error);

    return writer.ToArray();
  }

  private sealed class ByteWriter : CodeWriter
  {
    private readonly List<byte> _bytes = new();
    public override void WriteByte(byte value) => _bytes.Add(value);
    public byte[] ToArray() => _bytes.ToArray();
  }
}
