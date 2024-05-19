/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text;
using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Win32.Injection;

internal static class CallStubAssembler
{
  private static readonly Guid CLSID_CLRRuntimeHost =
    new Guid(0x90F1A06E, 0x7712, 0x4762, 0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02);

  private static readonly Guid IID_ICLRRuntimeHost =
    new Guid(0x90F1A06C, 0x7712, 0x4762, 0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02);


  /// <summary>
  /// Adds a call stub to the assembler code.
  /// </summary>
  /// <param name="c">The assembler instance.</param>
  /// <param name="regAddr">The address of the register.</param>
  /// <param name="arguments">The array of arguments.</param>
  /// <param name="x86">A flag indicating whether the architecture is x86.</param>
  /// <param name="cleanStack">A flag indicating whether to clean the stack.</param>
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
  /// <param name="regFun">The register containing the function to call.</param>
  /// <param name="arguments">The arguments to pass to the function.</param>
  /// <param name="x86">A flag indicating whether the code is targeting x86 architecture.</param>
  /// <param name="cleanStack">A flag indicating whether to clean the stack after the call.</param>
  private static void AddCallStub(
    Assembler c,
    Register regFun,
    object[] arguments,
    bool x86,
    bool cleanStack = false)
  {
    if (x86)
    {
      // push arguments
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

      // push the temp register so we can use it
      c.push(tempReg);

      // set arguments
      for (int i = arguments.Length - 1; i >= 0; i--) {
        var arg = arguments[i];
        var argReg = i switch { 0 => rcx, 1 => rdx, 2 => r8, 3 => r9, _ => default };
        if (i > 3)
        {
          // push on the stack, keeping in mind that we pushed the temp reg onto the stack too
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
          // move to correct register
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

      // pop temp register again
      c.pop(tempReg);

      // call the function
      c.call(new AssemblerRegister64(regFun));
    }
  }

  /// <summary>
  /// Creates a call stub for executing a method in a separate process.
  /// </summary>
  /// <param name="hProc">The handle to the target process.</param>
  /// <param name="asmPath">The path to the assembly containing the method to be executed.</param>
  /// <param name="typeFullName">The full name of the type containing the method to be executed.</param>
  /// <param name="methodName">The name of the method to be executed.</param>
  /// <param name="args">The arguments to be passed to the method.</param>
  /// <param name="fnAddr">The address of the function to be called in the target process.</param>
  /// <param name="x86">A flag indicating whether the target process is 32-bit or 64-bit.</param>
  /// <param name="clrVersion">The version of the Common Language Runtime (CLR) to be used.</param>
  /// <returns>A read-only list of instructions representing the call stub.</returns>
  public static IReadOnlyList<Instruction> CreateCallStub(
    IntPtr hProc,
    string asmPath,
    string typeFullName,
    string methodName,
    string? args,
    IntPtr fnAddr,
    bool x86,
    string clrVersion)
  {
    const string buildFlavor = "wks"; // Workstation (Default)

    // Create local functions to allocate native memory and write to it
    IntPtr alloc(int size, int protection = 0x04) =>
      Kernel32.VirtualAllocEx(hProc, IntPtr.Zero, (uint)size, 0x1000, protection);
    void writeBytes(IntPtr address, byte[] b) =>
      Kernel32.WriteProcessMemory(hProc, address, b, (uint)b.Length, out _);

    IntPtr allocString(string? str)
    {
      if (str is null) return IntPtr.Zero;

      IntPtr pString = alloc(str.Length * 2 + 2);
      writeBytes(pString, new UnicodeEncoding().GetBytes(str));

      return pString;
    }

    IntPtr allocBytes(byte[] buffer)
    {
      IntPtr pBuffer = alloc(buffer.Length);
      writeBytes(pBuffer, buffer);
      return pBuffer;
    }

    var ppv = alloc(IntPtr.Size);
    var riid = allocBytes(IID_ICLRRuntimeHost.ToByteArray());
    var rcslid = allocBytes(CLSID_CLRRuntimeHost.ToByteArray());
    var pwszBuildFlavor = allocString(buildFlavor);
    var pwszVersion = allocString(clrVersion);

    var pReturnValue = alloc(4);
    var pwzArgument = allocString(args);
    var pwzMethodName = allocString(methodName);
    var pwzTypeName = allocString(typeFullName);
    var pwzAssemblyPath = allocString(asmPath);

    var c = new Assembler(x86 ? 32 : 64);

    // Create local functions to pass arguments to AddCallStub
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

    return c.Instructions;
  }
}
