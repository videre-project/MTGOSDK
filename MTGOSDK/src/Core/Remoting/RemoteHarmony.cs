/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

using MessagePack;

using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting;

public delegate void HookAction(HookContext context, dynamic instance, dynamic[] args);

public class RemoteHarmony
{
  public sealed class HarmonyDiagnostics
  {
    public int MethodEntries { get; set; }
    public int CallbackEntries { get; set; }
    public int WrappedHookEntries { get; set; }
  }

  private readonly RemoteHandle _app;

  private readonly ConcurrentDictionary<MethodBase, MethodHooks> _callbacksToProxies;

  /// <summary>
  /// A LocalHookCallback in a specific patching position
  /// </summary>
  private class PositionedLocalHook
  {
    public HookAction HookAction { get; set; }
    public LocalHookCallback WrappedHookAction { get; set; }
    public HarmonyPatchPosition Position { get; private set; }
    public RemoteHookCapture Capture { get; set; }
    public PositionedLocalHook(HookAction action, HarmonyPatchPosition pos)
    {
      HookAction = action;
      Position = pos;
    }
  }

  private class MethodHooks : ConcurrentDictionary<HookAction, PositionedLocalHook>
  {
  }

  internal RemoteHarmony(RemoteHandle app)
  {
    _app = app;
    _callbacksToProxies = new ConcurrentDictionary<MethodBase, MethodHooks>();
  }

  /// <returns>True on success, false otherwise</returns>

  public bool HookMethod(MethodBase methodToHook, HarmonyPatchPosition pos, HookAction hookAction,
    RemoteHookCapture capture = null)
  {
    // Get or create MethodHooks object for the given method
    MethodHooks methodHooks = _callbacksToProxies.GetOrAdd(methodToHook, _ => new MethodHooks());

    lock (methodHooks)
    {
      bool hasHooks = methodHooks.Count > 0;

      if (methodHooks.TryGetValue(hookAction, out PositionedLocalHook registeredHook))
      {
        if (registeredHook.Position != pos)
        {
          throw new InvalidOperationException("Hook already exists on another patch type.");
        }

        if (!MessagePackSerializer.Serialize(registeredHook.Capture)
            .SequenceEqual(MessagePackSerializer.Serialize(capture)))
          throw new InvalidOperationException("Hook capture differs from the existing subscription.");
        return true;
      }

      // Handle multiple hooks on the same method
      if (hasHooks)
      {
        // Enumerate all method hooks registered to find any matches based on position
        var existingHook = methodHooks.Values.FirstOrDefault(hook => hook.Position == pos);
        if (existingHook != null)
        {
          if (!MessagePackSerializer.Serialize(existingHook.Capture)
              .SequenceEqual(MessagePackSerializer.Serialize(capture)))
            throw new InvalidOperationException("Hook capture differs from the existing subscription.");
          // Keep the existing registered callback token stable and update the
          // delegate it dispatches. Replacing WrappedHookAction here would strand
          // the old callback token in the communicator/Diver maps.
          HookAction mergedHook = Delegate.Combine(existingHook.HookAction, hookAction) as HookAction;
          existingHook.HookAction = mergedHook;
          methodHooks.TryAdd(hookAction, existingHook);

          return true;
        }
      }

      // Wrapping the callback which uses `dynamic`s in a callback that handles `ObjectOrRemoteAddresses`
      // and converts them to DROs
      var positionedHook = new PositionedLocalHook(hookAction, pos) { Capture = capture };
      LocalHookCallback wrappedHook = WrapCallback(positionedHook);
      positionedHook.WrappedHookAction = wrappedHook;
      methodHooks.TryAdd(hookAction, positionedHook);

      var parametersTypeFullNames = methodToHook.GetParameters()
        .Select(prm => prm.ParameterType.FullName)
        .ToList();

      return _app.Communicator.HookMethod(
          methodToHook.DeclaringType.FullName,
          methodToHook.Name,
          pos,
          wrappedHook,
          parametersTypeFullNames, capture);
    }
  }

  private LocalHookCallback WrapCallback(PositionedLocalHook hook)
  {
    LocalHookCallback hookProxy = (HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args) =>
    {
      // This client owns one pin reference to each object argument: resolving
      // it takes the reference over; anything left unresolved returns it.
      if (hook.Capture?.IncludeParameters == false)
      {
        _app.Communicator.ReleaseCallbackArguments(context, [instance, .. args]);
        hook.HookAction.DynamicInvoke(context, null, Array.Empty<object>());
        return;
      }

      DynamicRemoteObject droInstance = null;
      RemoteObject ro;
      ObjectOrRemoteAddress[] owned = [instance, .. args];
      int settled = 0; // owned[..settled] were taken over (or returned on failure)
      try
      {
        settled = 1;
        if (!instance.IsNull)
        {
          RemoteObject roInstance = this._app.GetRemoteObjectFromField(instance,
            context.OwnsParameters);
          droInstance = roInstance.Dynamify();
          droInstance.__timestamp = context.Timestamp; // Preserve oora timestamp
        }

        if (args.Length != 1)
        {
          throw new NotImplementedException("Unexpected arguments forwarded to callback from the diver.");
        }
        // The Diver passes method arguments as one remote object array. Decode
        // its elements into local values and proxies before invoking the callback.
        settled = 2;
        ro = _app.GetRemoteObjectFromField(args[0], context.OwnsParameters);
      }
      finally
      {
        _app.Communicator.ReleaseCallbackArguments(context, owned.Skip(settled));
      }
      dynamic dro = ro.Dynamify();
      if (!ro.GetType().IsArray)
      {
        throw new NotImplementedException("Unexpected arguments forwarded to callback from the diver -- single arg but not an array.");
      }

      int len = 0;
      try { len = (int) dro.Length; }
      catch { }

      object[] decodedParameters = new object[len];
      for (int i = 0; i < len; i++)
      {
        // The argument array lives in the target process, so each element
        // must be fetched through its proxy.
        dynamic item = dro[i];
        decodedParameters[i] = item;
        if (item is DynamicRemoteObject droItem)
        {
          droItem.__timestamp = args[0].Timestamp;
        }
      }

      hook.HookAction.DynamicInvoke(context, droInstance, decodedParameters);
    };
    return hookProxy;
  }

  public void Patch(MethodBase original,
    HookAction prefix = null,
    HookAction postfix = null,
    HookAction finalizer = null)
  {
    if (prefix == null && postfix == null && finalizer == null)
    {
      throw new ArgumentException("No hooks defined.");
    }

    if (prefix != null)
    {
      HookMethod(original, HarmonyPatchPosition.Prefix, prefix);
    }
    if (postfix != null)
    {
      HookMethod(original, HarmonyPatchPosition.Postfix, postfix);
    }
    if (finalizer != null)
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

    lock (hooks)
    {
      try
      {
        // Check if there are multiple delegates in the callback's invocation list
        if (positionedHookWrapper.HookAction.GetInvocationList().Length > 1)
        {
          // Remove the delegate from the invocation list. The registered wrapper
          // remains the same and will dispatch the updated HookAction.
          var previousHook = Delegate.Remove(positionedHookWrapper.HookAction, callback) as HookAction;
          positionedHookWrapper.HookAction = previousHook;
        }
        // Otherwise, remove the entire hook
        else
        {
          _app.Communicator.UnhookMethod(positionedHookWrapper.WrappedHookAction);
        }
      }
      finally
      {
        hooks.TryRemove(callback, out _);
        if (hooks.Count == 0)
        {
          // It was the last hook for this method, need to remove the inner dict
          _callbacksToProxies.TryRemove(methodToHook, out _);
        }
      }

      return true;
    }
  }

  public bool HasHook(MethodBase methodToHook, HookAction callback)
  {
    if (!_callbacksToProxies.TryGetValue(methodToHook, out MethodHooks hooks))
    {
      return false;
    }

    return hooks.ContainsKey(callback);
  }

  public bool HasHook(
    MethodBase methodToHook,
    HookAction callback,
    HarmonyPatchPosition position)
  {
    if (!_callbacksToProxies.TryGetValue(methodToHook, out MethodHooks hooks))
    {
      return false;
    }

    return hooks.TryGetValue(callback, out PositionedLocalHook hook) &&
      hook.Position == position;
  }

  internal HarmonyDiagnostics GetDiagnostics()
  {
    var diagnostics = new HarmonyDiagnostics
    {
      MethodEntries = _callbacksToProxies.Count,
    };

    foreach (var hooks in _callbacksToProxies.Values)
    {
      diagnostics.CallbackEntries += hooks.Count;
      diagnostics.WrappedHookEntries += hooks.Values
        .Select(hook => hook.WrappedHookAction)
        .Distinct()
        .Count();
    }

    return diagnostics;
  }
}
