/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using MTGOSDK.Win32.API;
using MTGOSDK.Win32.Extensions;
using MTGOSDK.Win32.Utilities;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Base class for injecting managed .NET Framework assemblies into a process.
/// </summary>
public class InjectorBase
{
  private const int MemCommit = 0x1000;
  private const int MemReserve = 0x2000;
  private const uint MemRelease = 0x8000;
  private const int PageReadWrite = 0x04;
  private const uint PageExecuteRead = 0x20;

  private static readonly Guid s_clrRuntimeHostClassId =
    new(0x90F1A06E, 0x7712, 0x4762, 0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02);

  private static readonly Guid s_clrRuntimeHostInterfaceId =
    new(0x90F1A06C, 0x7712, 0x4762, 0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02);

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
  /// Starts a remote thread to load a managed assembly and invoke a static
  /// method in the default AppDomain.
  /// </summary>
  /// <param name="process">The target process, with the .NET Framework CLR loaded.</param>
  /// <param name="dllPath">The assembly path in the target process.</param>
  /// <param name="typeName">The fully qualified entry-point type name.</param>
  /// <param name="methodName">The static method accepting a string and returning an int.</param>
  /// <param name="argument">The string passed to the entry point, or null.</param>
  /// <remarks>
  /// Returns after thread creation without waiting for CLR activation or
  /// entry-point completion. Callers must check entry-point success separately.
  /// Bootstrap memory remains allocated until the target process exits.
  /// </remarks>
  /// <exception cref="ArgumentNullException">The process is null.</exception>
  /// <exception cref="ArgumentException">A required assembly or entry-point name is empty.</exception>
  /// <exception cref="InvalidOperationException">A target module is missing or a memory write is incomplete.</exception>
  /// <exception cref="PlatformNotSupportedException">The target architecture is unsupported.</exception>
  /// <exception cref="Win32Exception">A process or memory operation failed.</exception>
  public void Inject(
    Process process,
    string dllPath,
    string typeName,
    string methodName,
    string? argument = null)
  {
    if (process is null) throw new ArgumentNullException(nameof(process));
    if (string.IsNullOrWhiteSpace(dllPath))
      throw new ArgumentException("An assembly path is required.", nameof(dllPath));
    if (string.IsNullOrWhiteSpace(typeName))
      throw new ArgumentException("An entry-point type is required.", nameof(typeName));
    if (string.IsNullOrWhiteSpace(methodName))
      throw new ArgumentException("An entry-point method is required.", nameof(methodName));

    IntPtr handle = Kernel32.OpenProcess(InjectionFlags, false, (uint)process.Id);
    if (handle == IntPtr.Zero)
      throw NativeError($"Opening target process {process.Id}");

    try
    {
      using var memory = new RemoteMemory(handle);
      var bindToRuntime = GetCorBindToRuntimeExAddress(process, handle, out var architecture);
      IntPtr registerFunctionTable = IntPtr.Zero;
      if (architecture == Architecture.Arm64)
      {
        var ntdll = GetLoadedModule(process, "ntdll.dll");
        registerFunctionTable = ntdll.modBaseAddr + PEReader.GetExportAddress(
          handle, ntdll.modBaseAddr, "RtlAddFunctionTable");
      }

      var arguments = CreateBootstrapArguments(
        memory, dllPath, typeName, methodName, argument, bindToRuntime,
        architecture, ClrVersion, registerFunctionTable);
      byte[] callStub = CallStubAssembler.CreateCallStub(arguments, architecture);
      var thread = RunRemoteCode(handle, memory, callStub);
      memory.Retain();
      Kernel32.CloseHandle(thread);
    }
    finally
    {
      Kernel32.CloseHandle(handle);
    }
  }

  /// <summary>
  /// Finds a module loaded in the target process.
  /// </summary>
  private static ModuleEntry32 GetLoadedModule(Process process, string name)
  {
    var module = process.GetModules().SingleOrDefault(entry =>
      entry.szModule.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (module.Equals(default(ModuleEntry32)))
      throw new InvalidOperationException(
        $"Target process {process.Id} has not loaded {name}. Ensure its CLR has started before injecting.");
    return module;
  }

  /// <summary>
  /// Resolves CorBindToRuntimeEx and identifies CLR architecture in the target process.
  /// </summary>
  private static IntPtr GetCorBindToRuntimeExAddress(
    Process process,
    IntPtr hProc,
    out Architecture architecture)
  {
    var module = GetLoadedModule(process, "mscoree.dll");
    // AnyCPU executable headers cannot distinguish x64 from ARM64.
    architecture = PEReader.GetArchitecture(hProc, module.modBaseAddr);
    CallStubAssembler.ValidateArchitecture(architecture);

    int offset = PEReader.GetExportAddress(
      hProc, module.modBaseAddr, "CorBindToRuntimeEx", architecture == Architecture.X86);
    return module.modBaseAddr + offset;
  }

  /// <summary>
  /// Writes CLR hosting arguments into target memory.
  /// </summary>
  private static ClrBootstrapArguments CreateBootstrapArguments(
    RemoteMemory memory,
    string asmPath,
    string typeFullName,
    string methodName,
    string? args,
    IntPtr fnAddr,
    Architecture architecture,
    string clrVersion,
    IntPtr registerFunctionTable = default)
  {
    IntPtr allocBytes(byte[] bytes)
    {
      IntPtr address = memory.Allocate(bytes.Length);
      memory.Write(address, bytes);
      return address;
    }

    IntPtr allocString(string? value)
    {
      if (value is null) return IntPtr.Zero;
      return allocBytes(Encoding.Unicode.GetBytes(value + "\0"));
    }

    var ppv = memory.Allocate(architecture == Architecture.X86 ? 4 : 8);
    var riid = allocBytes(s_clrRuntimeHostInterfaceId.ToByteArray());
    var classId = allocBytes(s_clrRuntimeHostClassId.ToByteArray());
    var buildFlavor = allocString("wks");
    var version = allocString(clrVersion);
    var returnValue = memory.Allocate(sizeof(uint));
    var argument = allocString(args);
    var method = allocString(methodName);
    var type = allocString(typeFullName);
    var assembly = allocString(asmPath);

    return new ClrBootstrapArguments(
      bindToRuntime: fnAddr,
      runtimeVersion: version,
      buildFlavor: buildFlavor,
      classId: classId,
      interfaceId: riid,
      runtimeHost: ppv,
      assemblyPath: assembly,
      typeName: type,
      methodName: method,
      argument: argument,
      returnValue: returnValue,
      registerFunctionTable: registerFunctionTable);
  }

  /// <summary>
  /// Writes generated code and starts a remote thread.
  /// </summary>
  private static IntPtr RunRemoteCode(IntPtr process, RemoteMemory memory, byte[] bytes)
  {
    IntPtr code = memory.Allocate(bytes.Length);
    memory.Write(code, bytes);
    if (!Kernel32.VirtualProtectEx(process, code, (nuint)bytes.Length, PageExecuteRead, out _))
      throw NativeError("Protecting the CLR injection stub");
    if (!Kernel32.FlushInstructionCache(process, code, (nuint)bytes.Length))
      throw NativeError("Flushing the CLR injection stub's instruction cache");

    var thread = Kernel32.CreateRemoteThread(
      process, IntPtr.Zero, 0u, code, IntPtr.Zero, 0u, IntPtr.Zero);
    if (thread == IntPtr.Zero)
      throw NativeError("Starting the CLR injection thread");
    return thread;
  }

  private static Win32Exception NativeError(string operation)
  {
    int error = Marshal.GetLastWin32Error();
    return new Win32Exception(error, $"{operation} failed: {new Win32Exception(error).Message} (Win32 {error}).");
  }

  /// <summary>
  /// Releases staged allocations if setup fails before thread creation.
  /// </summary>
  private sealed class RemoteMemory(IntPtr process) : IDisposable
  {
    private readonly List<IntPtr> _allocations = new();
    private bool _retained;

    public IntPtr Allocate(int size)
    {
      IntPtr address = Kernel32.VirtualAllocEx(
        process, IntPtr.Zero, (uint)size, MemCommit | MemReserve, PageReadWrite);
      if (address == IntPtr.Zero) throw NativeError("Allocating CLR injection memory");
      _allocations.Add(address);
      return address;
    }

    public void Write(IntPtr address, byte[] bytes)
    {
      if (!Kernel32.WriteProcessMemory(process, address, bytes, (nuint)bytes.Length, out var written))
        throw NativeError("Writing CLR injection memory");
      if (written != (nuint)bytes.Length)
        throw new InvalidOperationException(
          $"Only {written} of {bytes.Length} CLR injection bytes were written to the target process.");
    }

    public void Retain() => _retained = true;

    public void Dispose()
    {
      // Remote calls can outlive Inject, and registered ARM64 unwind tables
      // retain references to stub memory.
      if (_retained) return;
      for (int index = _allocations.Count - 1; index >= 0; index--)
        Kernel32.VirtualFreeEx(process, _allocations[index], 0, MemRelease);
    }
  }
}
