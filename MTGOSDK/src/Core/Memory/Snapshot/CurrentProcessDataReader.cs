/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Core.Memory.Snapshot;

/// <summary>
/// Reads the current Windows process for in-process CLR inspection.
/// </summary>
/// <remarks>
/// Uses live memory on native Windows and Wine. Callers are responsible for
/// coordinating CLR access and garbage collection; this reader does not suspend
/// threads or create a process snapshot.
/// </remarks>
internal sealed class CurrentProcessDataReader : IDataReader
{
  private const uint ThreadGetContext = 0x0008;
  private const int ContextAlignment = 16;

  private readonly DataTargetLimits _limits;
  private readonly IntPtr _processHandle;
  private readonly int _contextSize;
  private readonly int _contextFlagsOffset;

  private CurrentProcessDataReader(DataTargetLimits limits)
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      throw new PlatformNotSupportedException(
        "In-process CLR inspection requires a Windows runtime, including Wine.");

    _limits = limits;
    (_contextSize, _contextFlagsOffset) = Architecture switch
    {
      Architecture.X86 => (Marshal.SizeOf<X86Context>(),
        (int)Marshal.OffsetOf<X86Context>(nameof(X86Context.ContextFlags))),
      Architecture.X64 => (Marshal.SizeOf<AMD64Context>(),
        (int)Marshal.OffsetOf<AMD64Context>(nameof(AMD64Context.ContextFlags))),
      Architecture.Arm64 => (Marshal.SizeOf<Arm64Context>(),
        (int)Marshal.OffsetOf<Arm64Context>(nameof(Arm64Context.ContextFlags))),
      _ => throw new PlatformNotSupportedException(
        $"In-process CLR inspection does not support {Architecture}.")
    };

    using var process = Process.GetCurrentProcess();
    ProcessId = process.Id;
    // Pseudo handles do not require CloseHandle.
    _processHandle = Kernel32.GetCurrentProcess();
  }

  /// <summary>
  /// Attaches ClrMD to this process without requiring Windows PSS APIs.
  /// </summary>
  /// <remarks>
  /// ClrMD rejects self-attachment through AttachToProcess. A custom reader
  /// preserves direct attachment on Windows and Wine without PSS APIs.
  /// </remarks>
  public static DataTarget AttachToCurrentProcess()
  {
    var options = new DataTargetOptions();
    return new DataTarget(new CurrentProcessDataReader(options.Limits), options);
  }

  /// <inheritdoc/>
  public string DisplayName => $"pid:{ProcessId:x}";

  /// <inheritdoc/>
  public bool IsThreadSafe => true;

  /// <inheritdoc/>
  public OSPlatform TargetPlatform => OSPlatform.Windows;

  /// <inheritdoc/>
  public Architecture Architecture => RuntimeInformation.ProcessArchitecture;

  /// <inheritdoc/>
  public int ProcessId { get; }

  /// <inheritdoc/>
  public int PointerSize => IntPtr.Size;

  /// <inheritdoc/>
  public IEnumerable<ModuleInfo> EnumerateModules()
  {
    using var process = Process.GetCurrentProcess();
    foreach (ProcessModule module in process.Modules)
    {
      ulong address = PointerSize == sizeof(uint)
        ? unchecked((uint)module.BaseAddress.ToInt32())
        : unchecked((ulong)module.BaseAddress.ToInt64());
      var info = ModuleInfo.TryCreate(this, address, module.FileName, _limits);
      if (info is not null) yield return info;
    }
  }

  /// <inheritdoc/>
  public unsafe int Read(ulong address, Span<byte> buffer)
  {
    ulong maxAddress = PointerSize == sizeof(uint) ? uint.MaxValue : ulong.MaxValue;
    if (buffer.IsEmpty || address > maxAddress - (ulong)(buffer.Length - 1))
      return 0;

    IntPtr source = PointerSize == sizeof(uint)
      ? new IntPtr(unchecked((int)address))
      : new IntPtr(unchecked((long)address));
    fixed (byte* destination = buffer)
    {
      // ERROR_PARTIAL_COPY may still return readable bytes to the DAC.
      Kernel32.ReadProcessMemory(_processHandle, source,
        (IntPtr)destination, (nuint)buffer.Length, out var read);
      return (int)read;
    }
  }

  /// <inheritdoc/>
  public unsafe bool Read<T>(ulong address, out T value) where T : unmanaged
  {
    Span<byte> buffer = stackalloc byte[sizeof(T)];
    if (Read(address, buffer) == buffer.Length)
    {
      value = MemoryMarshal.Read<T>(buffer);
      return true;
    }
    value = default;
    return false;
  }

  /// <inheritdoc/>
  public T Read<T>(ulong address) where T : unmanaged =>
    Read(address, out T value) ? value : default;

  /// <inheritdoc/>
  public bool ReadPointer(ulong address, out ulong value)
  {
    if (PointerSize == sizeof(uint))
    {
      bool success = Read(address, out uint pointer);
      value = pointer;
      return success;
    }
    return Read(address, out value);
  }

  /// <inheritdoc/>
  public ulong ReadPointer(ulong address) =>
    ReadPointer(address, out ulong value) ? value : 0;

  /// <inheritdoc/>
  public unsafe bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
  {
    if (context.Length < _contextSize) return false;
    IntPtr thread = Kernel32.OpenThread(ThreadGetContext, false, threadID);
    if (thread == IntPtr.Zero) return false;
    try
    {
      // AMD64 and ARM64 CONTEXT buffers require 16-byte alignment.
      byte* storage = stackalloc byte[_contextSize + ContextAlignment - 1];
      byte* aligned = (byte*)(((nuint)storage + ContextAlignment - 1) &
        ~(nuint)(ContextAlignment - 1));
      var buffer = new Span<byte>(aligned, _contextSize);
      buffer.Clear();
      *(uint*)(aligned + _contextFlagsOffset) = contextFlags;
      if (!Kernel32.GetThreadContext(thread, (IntPtr)aligned)) return false;
      buffer.CopyTo(context);
      return true;
    }
    finally
    {
      Kernel32.CloseHandle(thread);
    }
  }

  /// <inheritdoc/>
  public void FlushCachedData() { } // Reads are uncached.
}
