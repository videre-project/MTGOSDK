/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using HarmonyLib;

using MTGOSDK.Core;
using MTGOSDK.Core.Remoting.Hooking;


namespace ScubaDiver.Hooking;

public class HarmonyWrapper
{
  // We'll use this class to indicate a some parameter in a hooked function can't be proxied
  public class DummyParameterReplacement
  {
    public static readonly DummyParameterReplacement Instance = new();

    public override string ToString() =>
      throw new InvalidOperationException("Parameter can't be proxied.");
  }

  private static HarmonyWrapper _instance = null;
  public static HarmonyWrapper Instance => _instance ??= new();

  private readonly Harmony _harmony;
  /// <summary>
  /// Maps 'target function parameters count' to right hook function (UnifiedHook_NUMBER)
  /// </summary>
  private readonly Dictionary<string, MethodInfo> _psHooks;
  /// <summary>
  /// Maps methods and the prefix hooks that were used to hook them. (Important for unpatching)
  /// </summary>
  private readonly Dictionary<string, MethodInfo> _singlePrefixHooks = new();

  /// <summary>
  /// This dict is static because <see cref="SinglePrefixHook"/> must be a static function (Harmony limitations)
  /// </summary>
  private static readonly ConcurrentDictionary<string, HookCallback> _actualHooks = new();

  private HarmonyWrapper()
  {
    _harmony = new Harmony("com.videre.mtgosdk");
    _psHooks = new Dictionary<string, MethodInfo>();
    var methods = typeof(HarmonyWrapper).GetMethods((BindingFlags)0xffffff);
    foreach (MethodInfo method in methods)
    {
      if (method.Name.StartsWith("UnifiedHook_"))
      {
        string key = method.Name.Substring("UnifiedHook_".Length);
        _psHooks[key] = method;
      }
    }
  }

  public static string GetUniqueId(MethodBase target) =>
    GetUniqueId(target.DeclaringType.FullName, target.Name);

  public static string GetUniqueId(string fullName, string methodName) =>
    fullName + ":" + methodName;

  public static bool HasCallback(MethodBase target) =>
    _actualHooks.ContainsKey(GetUniqueId(target));

  public static bool HasCallback(string uniqueId) =>
    _actualHooks.ContainsKey(uniqueId);

  public delegate Task HookCallback(object instance, object[] args);

  public void AddHook(MethodBase target, HarmonyPatchPosition pos, HookCallback patch)
  {
    static bool IsRefStruct(Type t)
    {
      var isByRefLikeProp = typeof(Type)
        .GetProperties()
        .FirstOrDefault(p => p.Name == "IsByRefLike");
      bool isDotNetCore = isByRefLikeProp != null;
      if(!isDotNetCore)
        return false;

      bool isByRef = t.IsByRef;
      bool isByRefLike = (bool)isByRefLikeProp.GetValue(t);
      if(isByRefLike)
        return true;

      if (!isByRef)
        return false;

      // Otherwise, we have a "ref" type. This could be things like:
      // Object&
      // Span<byte>&
      // We must look into the inner type to figure out if it's
      // RefLike (e.g., Span<byte>) or not (e.g., Object)
      return IsRefStruct(t.GetElementType());
    }

    //
    // Save the patch callback to invoke when the target is called
    //
    string uniqueId = GetUniqueId(target);
    _actualHooks[uniqueId] = patch;

    //
    // Actual hook for the method is the generic "SinglePrefixHook" (through one
    // if its proxies 'UnifiedHook_X')  the "SinglePrefixHook" will search for
    // the above saved callback and invoke it itself.
    //
    var parameters = target.GetParameters();
    int paramsCount = parameters.Length;
    int[] hookableParametersFlags = new int[10];
    for (int i = 0; i < paramsCount; i++)
    {
      ParameterInfo parameter = parameters[i];
      bool isRefStruct = IsRefStruct(parameter.ParameterType);
      hookableParametersFlags[i] = isRefStruct ? 0 : 1;
    }

    // Now we need to turn the parameters flags into a "binary" string.
    // For example:
    // {0, 0, 1, 0} ---> "0010"
    string binaryParams = string.Join(string.Empty, hookableParametersFlags.Select(i => i.ToString()));
    // Logger.Debug($"[HarmonyWrapper][AddHook] Constructed binaryParams: {binaryParams} for method {target.Name}");

    MethodInfo myPrefixHook;
    if (target.IsConstructor)
    {
      myPrefixHook = typeof(HarmonyWrapper).GetMethod("UnifiedHook_ctor", (BindingFlags)0xffff);
    }
    else
    {
      myPrefixHook = _psHooks[binaryParams];
    }

    // Document the `single prefix hook` used so we can remove later
    _singlePrefixHooks[uniqueId] = myPrefixHook;

    HarmonyMethod prefix = null;
    HarmonyMethod postfix = null;
    HarmonyMethod transpiler = null;
    HarmonyMethod finalizer = null;
    switch (pos)
    {
      case HarmonyPatchPosition.Prefix:
        prefix = new HarmonyMethod(myPrefixHook);
        break;
      case HarmonyPatchPosition.Postfix:
        postfix = new HarmonyMethod(myPrefixHook);
        break;
      case HarmonyPatchPosition.Finalizer:
        finalizer = new HarmonyMethod(myPrefixHook);
        break;
      default:
        throw new ArgumentException("Invalid Harmony patch position.");
    }
    _harmony.Patch(target, prefix, postfix, transpiler, finalizer);
  }

  public void RemovePrefix(MethodBase target)
  {
    string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
    if (_singlePrefixHooks.TryGetValue(uniqueId, out MethodInfo spHook))
    {
      _harmony.Unpatch(target, spHook);
    }
    _singlePrefixHooks.Remove(uniqueId);
    _actualHooks.TryRemove(uniqueId, out _);
  }

  private static async Task SinglePrefixHook(MethodBase __originalMethod, object __instance, params object[] args)
  {
    string uniqueId = __originalMethod.DeclaringType.FullName + ":"
                    + __originalMethod.Name;
    if (_actualHooks.TryGetValue(uniqueId, out HookCallback funcHook))
    {
      await SyncThread.EnqueueAsync(() => funcHook(__instance, args), uniqueId);
    }
  }

#pragma warning disable IDE0051, CS4014
  // ReSharper disable UnusedMember.Local
  private static void UnifiedHook_ctor(MethodBase __originalMethod) =>
    SinglePrefixHook(__originalMethod, new object());
  private static void UnifiedHook_0000000000(MethodBase __originalMethod, object __instance) =>
    SinglePrefixHook(__originalMethod, __instance);
  private static void UnifiedHook_1000000000(MethodBase __originalMethod, object __instance, object __0) =>
    SinglePrefixHook(__originalMethod, __instance, __0);
  private static void UnifiedHook_1100000000(MethodBase __originalMethod, object __instance, object __0, object __1) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1);
  private static void UnifiedHook_0100000000(MethodBase __originalMethod, object __instance, object __1) =>
    SinglePrefixHook(__originalMethod, __instance, DummyParameterReplacement.Instance, __1);
  private static void UnifiedHook_1110000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2);
  private static void UnifiedHook_0110000000(MethodBase __originalMethod, object __instance, object __1, object __2) =>
    SinglePrefixHook(__originalMethod, __instance, DummyParameterReplacement.Instance, __1, __2);
  private static void UnifiedHook_1010000000(MethodBase __originalMethod, object __instance, object __0, object __2) =>
    SinglePrefixHook(__originalMethod, __instance, __0, DummyParameterReplacement.Instance, __2);
  private static void UnifiedHook_0010000000(MethodBase __originalMethod, object __instance, object __2) =>
    SinglePrefixHook(__originalMethod, __instance, DummyParameterReplacement.Instance, DummyParameterReplacement.Instance, __2);
  private static void UnifiedHook_1111000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3);
  private static void UnifiedHook_1111100000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4);
  private static void UnifiedHook_1111110000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
  private static void UnifiedHook_1111111000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
  private static void UnifiedHook_1111111100(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
  private static void UnifiedHook_1111111110(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
  private static void UnifiedHook_1111111111(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9) =>
    SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
  // ReSharper restore UnusedMember.Local
#pragma warning restore IDE0051, CS4014
}
