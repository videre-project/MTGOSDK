/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;

using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Hooking;


namespace ScubaDiver.Hooking;

public class HarmonyWrapper
{
  /// <summary>
  /// Occupies the argument slot of a byref-like value that cannot be boxed.
  /// </summary>
  public class DummyParameterReplacement
  {
    public static readonly DummyParameterReplacement Instance = new();

    public override string ToString() =>
      throw new InvalidOperationException("Parameter can't be proxied.");
  }

  private static readonly Lazy<HarmonyWrapper> s_instance = new(() => new());
  public static HarmonyWrapper Instance => s_instance.Value;

  private readonly Harmony _harmony;

  /// <summary>
  /// Selects a Harmony adapter from the target's boxable argument positions.
  /// </summary>
  private readonly Dictionary<string, MethodInfo> _parameterHooks;

  /// <summary>
  /// Retains the installed adapter so removal targets only this wrapper's patch.
  /// </summary>
  private readonly ConcurrentDictionary<MethodBase, MethodInfo> _installedHooks = new();

  /// <summary>
  /// Routes Harmony's static adapters to the registered in-process callbacks.
  /// </summary>
  private static readonly ConcurrentDictionary<MethodBase, HookCallback> s_callbacks = new();
  private static readonly ConcurrentDictionary<MethodBase, Func<object, object[], Action<Exception>>>
    s_scopeHooks = new();

  private HarmonyWrapper()
  {
    _harmony = new Harmony("com.videre.mtgosdk");
    _parameterHooks = new Dictionary<string, MethodInfo>();
    var methods = typeof(HarmonyWrapper).GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
    foreach (MethodInfo method in methods)
    {
      if (method.Name.StartsWith("UnifiedHook_"))
      {
        string key = method.Name.Substring("UnifiedHook_".Length);
        _parameterHooks[key] = method;
      }
    }
  }

  public static bool HasCallback(MethodBase target) =>
    s_callbacks.ContainsKey(target) || s_scopeHooks.ContainsKey(target);

  public delegate void HookCallback(object instance, object[] args);

  public void AddHook(MethodBase target, HarmonyPatchPosition pos, HookCallback patch)
  {
    static bool IsRefStruct(Type t)
    {
      var isByRefLikeProp = typeof(Type)
        .GetProperties()
        .FirstOrDefault(p => p.Name == "IsByRefLike");
      bool isDotNetCore = isByRefLikeProp != null;
      if (!isDotNetCore)
        return false;

      bool isByRef = t.IsByRef;
      bool isByRefLike = (bool)isByRefLikeProp.GetValue(t);
      if (isByRefLike)
        return true;

      if (!isByRef)
        return false;

      // By-reference parameters expose the element type's boxing restriction.
      return IsRefStruct(t.GetElementType());
    }


    // The adapter preserves argument positions when a byref-like value cannot
    // be boxed. Unsupported signatures are rejected before registering a callback.
    var parameters = target.GetParameters();
    int paramsCount = parameters.Length;
    if (!target.IsConstructor && paramsCount > 10)
      throw new NotSupportedException("Method hooks support at most ten parameters.");
    int[] hookableParametersFlags = new int[10];
    for (int i = 0; !target.IsConstructor && i < paramsCount; i++)
    {
      ParameterInfo parameter = parameters[i];
      bool isRefStruct = IsRefStruct(parameter.ParameterType);
      hookableParametersFlags[i] = isRefStruct ? 0 : 1;
    }

    // Adapter names encode ten argument positions, with zero for omitted values.
    string binaryParams = string.Join(string.Empty, hookableParametersFlags.Select(i => i.ToString()));

    MethodInfo hookMethod;
    if (target.IsConstructor)
    {
      hookMethod = typeof(HarmonyWrapper).GetMethod("UnifiedHook_ctor", BindingFlags.NonPublic | BindingFlags.Static);
    }
    else
    {
      if (!_parameterHooks.TryGetValue(binaryParams, out hookMethod))
        throw new NotSupportedException($"No hook adapter supports the parameter signature of {target}.");
    }

    HarmonyMethod prefix = null;
    HarmonyMethod postfix = null;
    HarmonyMethod transpiler = null;
    HarmonyMethod finalizer = null;
    switch (pos)
    {
      case HarmonyPatchPosition.Prefix:
        prefix = new HarmonyMethod(hookMethod);
        break;
      case HarmonyPatchPosition.Postfix:
        postfix = new HarmonyMethod(hookMethod);
        break;
      case HarmonyPatchPosition.Finalizer:
        finalizer = new HarmonyMethod(hookMethod);
        break;
      default:
        throw new ArgumentException("Invalid Harmony patch position.");
    }
    if (!s_callbacks.TryAdd(target, patch))
      throw new InvalidOperationException("A callback is already installed for this method.");
    _installedHooks[target] = hookMethod;
    try
    {
      _harmony.Patch(target, prefix, postfix, transpiler, finalizer);
    }
    catch
    {
      s_callbacks.TryRemove(target, out _);
      _installedHooks.TryRemove(target, out _);
      throw;
    }
  }

  public void RemoveHook(MethodBase target)
  {
    if (_installedHooks.TryGetValue(target, out MethodInfo hookMethod))
    {
      _harmony.Unpatch(target, hookMethod);
    }
    _installedHooks.TryRemove(target, out _);
    s_callbacks.TryRemove(target, out _);
    if (s_scopeHooks.TryRemove(target, out _))
    {
      _harmony.Unpatch(target, typeof(HarmonyWrapper).GetMethod(nameof(ScopePrefix), BindingFlags.NonPublic | BindingFlags.Static));
      _harmony.Unpatch(target, typeof(HarmonyWrapper).GetMethod(nameof(ScopeFinalizer), BindingFlags.NonPublic | BindingFlags.Static));
    }
  }

  /// <summary>
  /// Installs a prefix that creates invocation-local state and a finalizer that
  /// completes it on normal return or exception. The prefix callback stays in process.
  /// </summary>
  public void AddScopeHook(MethodBase target, Func<object, object[], Action<Exception>> enter)
  {
    if (!s_scopeHooks.TryAdd(target, enter))
      throw new InvalidOperationException("A capture scope is already installed for this method.");
    try
    {
      var flags = BindingFlags.NonPublic | BindingFlags.Static;
      _harmony.Patch(target,
        prefix: new HarmonyMethod(typeof(HarmonyWrapper).GetMethod(nameof(ScopePrefix), flags)),
        finalizer: new HarmonyMethod(typeof(HarmonyWrapper).GetMethod(nameof(ScopeFinalizer), flags)));
    }
    catch
    {
      s_scopeHooks.TryRemove(target, out _);
      throw;
    }
  }

  private static void ScopePrefix(MethodBase __originalMethod, object __instance,
    object[] __args, out Action<Exception> __state)
  {
    __state = null;
    try
    {
      if (s_scopeHooks.TryGetValue(__originalMethod, out var enter))
        __state = enter(__instance, __args);
    }
    catch (Exception error)
    {
      Log.Error(error, "[Diver] Failed to enter capture scope for {0}.", __originalMethod);
    }
  }

  private static void ScopeFinalizer(Action<Exception> __state, Exception __exception)
  {
    // Capture failures must not replace or suppress the native method's exception.
    try
    {
      __state?.Invoke(__exception);
    }
    catch (Exception error)
    {
      Log.Error(error, "[Diver] Failed to complete native capture scope.");
    }
  }

  private static void InvokeHook(
    MethodBase __originalMethod,
    object __instance,
    params object[] args)
  {
    if (s_callbacks.TryGetValue(__originalMethod, out HookCallback funcHook))
    {
      funcHook(__instance, args);
    }
  }

#pragma warning disable IDE0051, CS4014
  // ReSharper disable UnusedMember.Local
  private static void UnifiedHook_ctor(
    MethodBase __originalMethod,
    object[] __args) =>
      InvokeHook(__originalMethod, new object(), __args);
  private static void UnifiedHook_0000000000(MethodBase __originalMethod, object __instance) =>
    InvokeHook(__originalMethod, __instance);
  private static void UnifiedHook_1000000000(MethodBase __originalMethod, object __instance, object __0) =>
    InvokeHook(__originalMethod, __instance, __0);
  private static void UnifiedHook_1100000000(MethodBase __originalMethod, object __instance, object __0, object __1) =>
    InvokeHook(__originalMethod, __instance, __0, __1);
  private static void UnifiedHook_0100000000(MethodBase __originalMethod, object __instance, object __1) =>
    InvokeHook(__originalMethod, __instance, DummyParameterReplacement.Instance, __1);
  private static void UnifiedHook_1110000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2);
  private static void UnifiedHook_0110000000(MethodBase __originalMethod, object __instance, object __1, object __2) =>
    InvokeHook(__originalMethod, __instance, DummyParameterReplacement.Instance, __1, __2);
  private static void UnifiedHook_1010000000(MethodBase __originalMethod, object __instance, object __0, object __2) =>
    InvokeHook(__originalMethod, __instance, __0, DummyParameterReplacement.Instance, __2);
  private static void UnifiedHook_0010000000(MethodBase __originalMethod, object __instance, object __2) =>
    InvokeHook(__originalMethod, __instance, DummyParameterReplacement.Instance, DummyParameterReplacement.Instance, __2);
  private static void UnifiedHook_1111000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3);
  private static void UnifiedHook_1111100000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3, __4);
  private static void UnifiedHook_1111110000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
  private static void UnifiedHook_1111111000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
  private static void UnifiedHook_1111111100(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
  private static void UnifiedHook_1111111110(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
  private static void UnifiedHook_1111111111(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9) =>
    InvokeHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
  // ReSharper restore UnusedMember.Local
#pragma warning restore IDE0051, CS4014
}
