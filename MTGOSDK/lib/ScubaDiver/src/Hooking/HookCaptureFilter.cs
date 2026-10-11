/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace ScubaDiver.Hooking;

/// <summary>
/// Binds caller-supplied filter types once and applies them on the native hook thread.
/// </summary>
internal sealed class HookCaptureFilter
{
  private sealed record Binding(Type Type, string Path, int Argument, HashSet<string> Values);

  private readonly Binding[] _bindings;
  private readonly BatchExecutor _executor;

  public HookCaptureFilter(IEnumerable<RemoteHookFilter> filters, MethodBase method,
    Func<string, Type> resolveType, BatchExecutor executor)
  {
    _executor = executor;
    var parameters = method.GetParameters();
    _bindings = filters.Select(filter =>
    {
      if (filter == null || string.IsNullOrWhiteSpace(filter.TypeFullName)
        || filter.ArgumentIndex < 0 || filter.ArgumentIndex >= parameters.Length
        || parameters[filter.ArgumentIndex].ParameterType != typeof(string)
        || filter.ArgumentValues == null || filter.ArgumentValues.Length == 0
        || (filter.InstancePath != null && filter.InstancePath.Split('.').Any(string.IsNullOrWhiteSpace)))
        throw new ArgumentException("Invalid native hook filter.");
      var type = resolveType(filter.TypeFullName)
        ?? throw new TypeLoadException(filter.TypeFullName);
      if (method.IsStatic || !method.DeclaringType.IsAssignableFrom(type))
        throw new ArgumentException("Hook filter type must implement the hooked instance method.");
      return new Binding(type, filter.InstancePath, filter.ArgumentIndex,
        new HashSet<string>(filter.ArgumentValues, StringComparer.Ordinal));
    }).ToArray();
  }

  /// <summary>
  /// Resolves the capture root only after the type and argument match. Rejected
  /// invocations perform no projection, pinning, or callback transport.
  /// </summary>
  public bool TryMatch(object instance, object[] arguments, out object captureRoot)
  {
    captureRoot = instance;
    if (_bindings.Length == 0) return true;
    foreach (var binding in _bindings)
    {
      if (!binding.Type.IsInstanceOfType(instance)
        || !binding.Values.Contains((string)arguments[binding.Argument]))
        continue;
      captureRoot = binding.Path == null ? instance : _executor.Read(instance, binding.Path);
      return captureRoot != null;
    }
    return false;
  }
}
