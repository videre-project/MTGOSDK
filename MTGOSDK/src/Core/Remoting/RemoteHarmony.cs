/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using System.Linq;


using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting;

public delegate void HookAction(HookContext context, dynamic instance, dynamic[] args);

public class RemoteHarmony
{
  private readonly RemoteHandle _app;

  private readonly Dictionary<MethodBase, MethodHooks> _callbacksToProxies;

  /// <summary>
  /// A LocalHookCallback in a specific patching position
  /// </summary>
  private class PositionedLocalHook
  {
    public HookAction HookAction { get; set; }
    public LocalHookCallback WrappedHookAction { get; set; }
    public HarmonyPatchPosition Position { get; private set; }
    public PositionedLocalHook(HookAction action, LocalHookCallback callback, HarmonyPatchPosition pos)
    {
      HookAction = action;
      WrappedHookAction = callback;
      Position = pos;
    }
  }

  private class MethodHooks : Dictionary<HookAction, PositionedLocalHook> {
  }

  internal RemoteHarmony(RemoteHandle app)
  {
    _app = app;
    _callbacksToProxies = new Dictionary<MethodBase, MethodHooks>();
  }

  /// <returns>True on success, false otherwise</returns>

  public bool HookMethod(MethodBase methodToHook, HarmonyPatchPosition pos, HookAction hookAction)
  {
    // Look for MethodHooks object for the given REMOTE OBJECT
    bool hasHooks = _callbacksToProxies.ContainsKey(methodToHook);
    MethodHooks methodHooks = hasHooks ? _callbacksToProxies[methodToHook] : new();

    // Handle multiple hooks on the same method
    LocalHookCallback wrappedHook = null!;
    if (hasHooks)
    {
      // Enumerate all method hooks registered to find any matches based on position
      var existingHook = methodHooks.Values.First(hook => hook.Position == pos);
      // Merge the HookedAction delegate and re-wrap it
      HookAction mergedHook = Delegate.Combine(existingHook.HookAction, hookAction) as HookAction;
      wrappedHook = WrapCallback(mergedHook);

      // Update the existing entry
      existingHook.HookAction = mergedHook;
      existingHook.WrappedHookAction = wrappedHook;

      // Create a new entry that references the existing one
      methodHooks.Add(hookAction, existingHook);

      return true;
    }

    // Wrapping the callback which uses `dynamic`s in a callback that handles `ObjectOrRemoteAddresses`
    // and converts them to DROs
    if(!methodHooks.ContainsKey(hookAction))
    {
      wrappedHook = WrapCallback(hookAction);
      methodHooks.Add(hookAction, new PositionedLocalHook(hookAction, wrappedHook, pos));
    }
    else
    {
      throw new InvalidOperationException("Hook already exists on another patch type.");
    }

    var parametersTypeFullNames = methodToHook.GetParameters()
      .Select(prm => prm.ParameterType.FullName)
      .ToList();

    return _app.Communicator.HookMethod(
        methodToHook.DeclaringType.FullName,
        methodToHook.Name,
        pos,
        wrappedHook,
        parametersTypeFullNames);
  }

  private LocalHookCallback WrapCallback(HookAction callback)
  {
    LocalHookCallback hookProxy = (HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args) =>
    {
      // Converting instance to DRO
      DynamicRemoteObject droInstance;
      if (instance.IsNull)
      {
        droInstance = null;
      }
      else
      {
        RemoteObject roInstance = this._app.GetRemoteObject(instance.RemoteAddress, instance.Type);
        droInstance = roInstance.Dynamify();
        droInstance.__timestamp = context.Timestamp; // Preserve oora timestamp
      }

      // Converting args to DROs/raw primitive types
      if (args.Length != 1)
      {
        throw new NotImplementedException("Unexpected arguments forwarded to callback from the diver.");
      }
      // We are expecting a single arg which is a REMOTE array of objects (object[]) and we need to flatten it
      // into several (Dynamic) Remote Objects in a LOCAL array of objects.
      RemoteObject ro = _app.GetRemoteObject(args[0].RemoteAddress, args[0].Type);
      dynamic dro = ro.Dynamify();
      if (!ro.GetType().IsArray)
      {
        throw new NotImplementedException("Unexpected arguments forwarded to callback from the diver -- single arg but not an array.");
      }

      int len = 0;
      try { len = (int)dro.Length; }
      catch { }

      object[] decodedParameters = new object[len];
      for (int i = 0; i < len; i++)
      {
        // Since this object isn't really a local array (just a proxy of a remote one) the index
        // acceess causes a 'GetItem' function call and retrieval of the remote object at the position
        dynamic item = dro[i];
        decodedParameters[i] = item;
        // (decodedParameters as dynamic)[i].__timestamp = args[i].Timestamp;
        if (item is DynamicRemoteObject droItem)
        {
          droItem.__timestamp = args[0].Timestamp;
        }
      }

      // Call the callback with the proxied parameters (using DynamicRemoteObjects)
      callback.DynamicInvoke(context, droInstance, decodedParameters);
    };
    return hookProxy;
  }

  public void Patch(MethodBase original,
    HookAction prefix = null,
    HookAction postfix = null,
    HookAction finalizer = null)
  {
    if(prefix == null && postfix == null && finalizer == null)
    {
      throw new ArgumentException("No hooks defined.");
    }

    if(prefix != null)
    {
      HookMethod(original, HarmonyPatchPosition.Prefix, prefix);
    }
    if(postfix != null)
    {
      HookMethod(original, HarmonyPatchPosition.Postfix, postfix);
    }
    if(finalizer != null)
    {
      HookMethod(original, HarmonyPatchPosition.Finalizer, finalizer);
    }
  }

  public bool UnhookMethod(MethodBase methodToHook, HookAction callback)
  {
    if (!_callbacksToProxies.TryGetValue(methodToHook, out MethodHooks hooks))
    {
      return false;
    }

    if (!hooks.TryGetValue(callback, out PositionedLocalHook positionedHookWrapper))
    {
      return false;
    }

    // Check if there are multiple delegates in the callback's invocation list
    if (positionedHookWrapper.HookAction.GetInvocationList().Length > 1)
    {
      // Remove the delegate from the invocation list
      var previousHook = Delegate.Remove(positionedHookWrapper.HookAction, callback) as HookAction;
      positionedHookWrapper.HookAction = previousHook;
    }
    // Otherwise, remove the entire hook
    else
    {
      _app.Communicator.UnhookMethod(positionedHookWrapper.WrappedHookAction);
    }

    hooks.Remove(callback);
    if (hooks.Count == 0)
    {
      // It was the last hook for this method, need to remove the inner dict
      _callbacksToProxies.Remove(methodToHook);
    }
    return true;
  }

  public bool HasHook(MethodBase methodToHook, HookAction callback)
  {
    if (!_callbacksToProxies.TryGetValue(methodToHook, out MethodHooks hooks))
    {
      return false;
    }

    return hooks.ContainsKey(callback);
  }
}
