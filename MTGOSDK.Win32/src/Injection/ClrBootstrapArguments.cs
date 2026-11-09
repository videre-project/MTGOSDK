/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.Win32.Injection;

/// <summary>
/// Function and argument addresses used by CLR injection stubs.
/// </summary>
internal sealed class ClrBootstrapArguments(
  IntPtr bindToRuntime,
  IntPtr runtimeVersion,
  IntPtr buildFlavor,
  IntPtr classId,
  IntPtr interfaceId,
  IntPtr runtimeHost,
  IntPtr assemblyPath,
  IntPtr typeName,
  IntPtr methodName,
  IntPtr argument,
  IntPtr returnValue,
  IntPtr registerFunctionTable = default)
{
  /// <summary>
  /// CorBindToRuntimeEx function address in target memory.
  /// </summary>
  public IntPtr BindToRuntime { get; } = bindToRuntime;

  /// <summary>
  /// Pointer to a null-terminated UTF-16 CLR version string.
  /// </summary>
  public IntPtr RuntimeVersion { get; } = runtimeVersion;

  /// <summary>
  /// Pointer to a null-terminated UTF-16 build flavor string.
  /// </summary>
  public IntPtr BuildFlavor { get; } = buildFlavor;

  /// <summary>
  /// Pointer to the CLR runtime host CLSID.
  /// </summary>
  public IntPtr ClassId { get; } = classId;

  /// <summary>
  /// Pointer to the ICLRRuntimeHost IID.
  /// </summary>
  public IntPtr InterfaceId { get; } = interfaceId;

  /// <summary>
  /// Address of an output slot for the ICLRRuntimeHost pointer.
  /// </summary>
  public IntPtr RuntimeHost { get; } = runtimeHost;

  /// <summary>
  /// Pointer to a null-terminated UTF-16 assembly path.
  /// </summary>
  public IntPtr AssemblyPath { get; } = assemblyPath;

  /// <summary>
  /// Pointer to a null-terminated UTF-16 entry-point type name.
  /// </summary>
  public IntPtr TypeName { get; } = typeName;

  /// <summary>
  /// Pointer to a null-terminated UTF-16 entry-point method name.
  /// </summary>
  public IntPtr MethodName { get; } = methodName;

  /// <summary>
  /// Pointer to a null-terminated UTF-16 method argument, or NULL.
  /// </summary>
  public IntPtr Argument { get; } = argument;

  /// <summary>
  /// Address of an output slot for the managed entry point's DWORD result.
  /// CLR hosting HRESULTs are returned by the stub.
  /// </summary>
  public IntPtr ReturnValue { get; } = returnValue;

  /// <summary>
  /// RtlAddFunctionTable function address, required for ARM64.
  /// </summary>
  public IntPtr RegisterFunctionTable { get; } = registerFunctionTable;
}
