/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Iced.Intel;

using MTGOSDK.Win32.API;
using MTGOSDK.Win32.Extensions;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Base class for injecting managed .NET Framework assemblies into a process.
/// </summary>
public class InjectorBase
{
  /// <summary>
  /// The .NET Framework CLR version to target.
  /// </summary>
  protected virtual string ClrVersion => "v4.0.30319";

  /// <summary>
  /// The process access flags to request for process injection.
  /// </summary>
  protected virtual ProcessAccessFlags InjectionFlags =>
    ProcessAccessFlags.CreateThread |
    ProcessAccessFlags.QueryInformation |
    ProcessAccessFlags.VirtualMemoryOperation |
    ProcessAccessFlags.VirtualMemoryRead |
    ProcessAccessFlags.VirtualMemoryWrite;

  /// <summary>
  /// Injects a managed assembly into a target process.
  /// </summary>
  /// <param name="process">The target process.</param>
  /// <param name="dllPath">The file path to the managed assembly.</param>
  /// <param name="typeName">The type name of the entry point.</param>
  /// <param name="methodName">The method name of the entry point.</param>
  /// <exception cref="Exception">
  /// Thrown if the target process has an architecture mismatch.
  /// </exception>
  public void Inject(
    Process process,
    string dllPath,
    string typeName,
    string methodName)
  {
    bool x86 = !process.Is64Bit();
    IntPtr handle = Kernel32.OpenProcess(InjectionFlags, false, (uint)process.Id);
    var bindToRuntimeAddr = GetCorBindToRuntimeExAddress(process, handle, x86);

    var callStub = CallStubAssembler.CreateCallStub(
      handle,
      dllPath,
      typeName,
      methodName,
      null,
      bindToRuntimeAddr,
      x86,
      ClrVersion
    );

    var hThread = RunRemoteCode(handle, callStub, x86);
  }

  /// <summary>
  /// Retrieves the address to CorBindToRuntimeEx in the target process.
  /// </summary>
  /// <param name="process">The target process to probe.</param>
  /// <param name="hProc">The handle to the target process.</param>
  /// <param name="x86">Whether the target process is 32-bit.</param>
  /// <returns></returns>
  /// <exception cref="Exception"></exception>
  private static IntPtr GetCorBindToRuntimeExAddress(
    Process process,
    IntPtr hProc,
    bool x86 = false)
  {
    // Find the mscoree.dll module in the target process.
    ModuleEntry32 module = process
      .GetModules()
      .SingleOrDefault(x =>
        x.szModule.Equals("mscoree.dll", StringComparison.InvariantCultureIgnoreCase));

    if (module.Equals(default(ModuleEntry32)))
      throw new Exception("Couldn't find mscoree.dll, possible arch mismatch?");

    // Retrieve the export address of the CorBindToRuntimeEx function.
    int fnAddr = PEReaderUtilities.GetExportAddress(
      hProc,
      module.modBaseAddr,
      "CorBindToRuntimeEx",
      x86
    );

    // Calculate the address of the function in the target process.
    return module.modBaseAddr + fnAddr;
  }

  /// <summary>
  /// Writes and executes instructions in the target process.
  /// </summary>
  /// <param name="hProc">The handle to the target process.</param>
  /// <param name="instructions">The instructions to execute.</param>
  /// <param name="x86">Whether the target process is 32-bit.</param>
  /// <returns>The handle to the remote thread.</returns>
  private static IntPtr RunRemoteCode(
    IntPtr hProc,
    IReadOnlyList<Instruction> instructions,
    bool x86 = false)
  {
    // Encode instructions into shellcode to run in the target process.
    var bw = new ByteWriter();
    var ib = new InstructionBlock(bw, new List<Instruction>(instructions), 0);
    if (!BlockEncoder.TryEncode(x86 ? 32 : 64, ib, out string? errMsg, out _))
      throw new Exception("Encountered an error during Iced encode: " + errMsg);

    // Allocate memory in the target process and write the shellcode.
    byte[] bytes = bw.ToArray();
    var ptrStub = Kernel32.VirtualAllocEx(
      hProc,
      IntPtr.Zero,
      (uint)bytes.Length,
      0x1000,
      0x40
    );
    Kernel32.WriteProcessMemory(
      hProc,
      ptrStub,
      bytes,
      (uint)bytes.Length,
      out _
    );

    // Create a remote thread and execute the shellcode.
    var thread = Kernel32.CreateRemoteThread(
      hProc,
      IntPtr.Zero,
      0u,
      ptrStub,
      IntPtr.Zero,
      0u,
      IntPtr.Zero
    );
    if (thread == IntPtr.Zero)
      throw new Exception("Failed to create remote thread.");

    return thread;
  }
}
