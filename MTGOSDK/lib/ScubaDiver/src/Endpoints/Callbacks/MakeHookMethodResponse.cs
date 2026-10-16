/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using MessagePack;

using MTGOSDK;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeHookMethodResponse()
  {
    Log.Debug("[Diver] Got Hook Method request!");

    var request = DeserializeRequest<HookSubscriptionRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    string typeFullName = request.TypeFullName;
    string methodName = request.MethodName;
    string hookPosition = request.HookPosition;
    if (!Enum.TryParse(hookPosition, out HarmonyPatchPosition pos) ||
        !Enum.IsDefined(typeof(HarmonyPatchPosition), pos))
      return QuickError("hook_position has an invalid or unsupported value");

    Type resolvedType = _runtime.ResolveType(typeFullName);
    if (resolvedType == null)
      return QuickError("Failed to resolve type");

    int paramCount = request.ParametersTypeFullNames?.Count ?? 0;
    Type[] paramTypes = new Type[paramCount];
    for (int i = 0; i < paramCount; i++)
    {
      paramTypes[i] = _runtime.ResolveType(request.ParametersTypeFullNames[i]);
    }

    MethodBase methodInfo = methodName == ".ctor"
      ? resolvedType.GetConstructor(
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
          binder: null, types: paramTypes, modifiers: null)
      : resolvedType.GetMethodRecursive(methodName, paramTypes);
    if (methodInfo == null)
      return QuickError($"Failed to find method {methodName} in type {resolvedType.Name}");

    Log.Debug($"[Diver] Hook Method - Resolved Method {methodInfo.Name}");

    HookCaptureFilter captureFilter = null;

    // Validate capture bindings before sharing or installing a native patch.
    if (request.Capture is { } captureDescription)
    {
      if (captureDescription.Batch == null || captureDescription.Batch.ForceUIThread ||
          captureDescription.Inputs == null || captureDescription.Batch.Inputs == null ||
          captureDescription.Inputs.Count > captureDescription.Batch.Inputs.Count ||
          captureDescription.Inputs.Any(index => index < -1 || index >= paramCount))
        return QuickError("Invalid hook capture inputs; capture runs on the native hook thread.");
      try
      {
        captureFilter = BindHookCapture(captureDescription, methodInfo, pos);
      }
      catch (Exception error)
      {
        return QuickError("Invalid hook capture: " + error.Message);
      }
    }

    lock (_methodHookGate)
    {
      var existingHook = _remoteHooks
        .FirstOrDefault(kvp => kvp.Value.OriginalHookedMethod == methodInfo);
      bool hasCallback = HarmonyWrapper.HasCallback(methodInfo);
      if (existingHook.Value != null || hasCallback)
      {
        Log.Debug($"[Diver] Hook Method - Found existing hook for {methodName}");
        if (_remoteHooks.TryGetValue(existingHook.Key, out RegisteredMethodHookInfo rmhi))
        {
          if (rmhi.Position != hookPosition ||
              !MessagePackSerializer.Serialize(rmhi.Capture)
                .SequenceEqual(MessagePackSerializer.Serialize(request.Capture)))
            return QuickError("An incompatible position or capture is already registered for this method.");
          // Callbacks are broadcast to connected clients. Share the token,
          // and retain the native patch for each subscription.
          Log.Debug("[Diver] Hook Method - Returning existing token");
          rmhi.SubscriptionCount++;
          AddClientSubscription(existingHook.Key, hook: true);
          var result = new EventRegistrationResults { Token = existingHook.Key };
          return WrapSuccess(result);
        }
        else
        {
          Log.Debug($"[Diver] Hook Method - Method {methodName} is already hooked, but no endpoint is associated with it.");
          Log.Debug($"[Diver] Hook Method - Removing old hook for {methodName}");
          HarmonyWrapper.Instance.RemoveHook(methodInfo);
        }
      }

      int token = AssignCallbackToken();
      _activeCallbacks[token] = 0;
      Log.Debug($"[Diver] Hook Method - Assigned Token: {token}");

      var registration = new RegisteredMethodHookInfo
      {
        OriginalHookedMethod = methodInfo,
        Position = hookPosition,
        Capture = request.Capture
      };

      var eventKey = (Type: resolvedType.FullName, Name: methodName);
      var completeCapture = CreateHookCaptureCallback(token, eventKey, request.Capture);
      HarmonyWrapper.HookCallback patchCallback = (obj, args) => completeCapture(obj, args, null, null);
      registration.RegisteredProxy = patchCallback;

      Log.Debug($"[Diver] Hooking function {methodName}...");
      try
      {
        var wrapper = HarmonyWrapper.Instance;
        if (request.Capture?.Scope != null)
        {
          wrapper.AddScopeHook(methodInfo,
            CreateHookScopeCallback(token, eventKey, request.Capture, captureFilter, completeCapture));
        }
        else wrapper.AddHook(methodInfo, pos, patchCallback);
      }
      catch (Exception ex)
      {
        _activeCallbacks.TryRemove(token, out _);
        Log.Debug($"[Diver] Failed to hook func {methodName}. Exception: {ex}");
        return QuickError("Failed insert the hook for the function. HarmonyWrapper.AddHook failed.");
      }

      Log.Debug($"[Diver] Hooked func {methodName}! token={token}, position={hookPosition}, capture={request.Capture != null}");
      _remoteHooks[token] = registration;
      AddClientSubscription(token, hook: true);

      var erResults = new EventRegistrationResults { Token = token };
      return WrapSuccess(erResults);
    }
  }

  //
  // Hook capture binding and validation.
  //

  private HookCaptureFilter BindHookCapture(RemoteHookCapture capture,
    MethodBase methodInfo, HarmonyPatchPosition position)
  {
    if (capture.Scope?.IsStateBoundary == true && !capture.CaptureOnExit)
      throw new ArgumentException("State boundaries must capture on completion.");
    BatchExecutor.Validate(capture.Batch);
    if (capture.Batch.ReferenceOutput != null)
      throw new ArgumentException("Callback captures require detached outputs.");
    CaptureScopeManager.Validate(capture.Scope, capture.Batch);
    if (capture.Scope != null &&
        capture.Scope.OwnerInput >= capture.Inputs.Count)
      throw new ArgumentException("The scope owner must be bound to a native hook parameter.");
    if (capture.Scope != null && (position != HarmonyPatchPosition.Postfix))
      throw new ArgumentException("Scoped hooks deliver completion captures at the postfix position.");
    capture.Filters ??= new();
    if (capture.Filters.Count != 0 && capture.Scope == null)
      throw new ArgumentException("Native capture filters require a capture scope.");
    return new HookCaptureFilter(capture.Filters, methodInfo,
      name => _runtime.ResolveType(name), _batchExecutor);
  }

  //
  // Native capture and invocation scope callbacks.
  //

  private Action<object, object[], RemoteCaptureSequence, Exception> CreateHookCaptureCallback(
    int token, (string Type, string Name) eventKey, RemoteHookCapture capture) =>
    (obj, args, sequence, nativeError) =>
    {
      if (!IsCallbackActive(token))
        return;

      DateTime timestamp = DateTime.UtcNow;
      dynamic mappedArgs = args;
      if (sequence == null && !GlobalEvents.IsValidEvent(eventKey, obj, args, out mappedArgs))
      {
        return;
      }

      RemoteBatchResult captured = null;
      RemoteBatchError captureError = null;
      if (capture != null)
      {
        captured = ExecuteCallbackCapture(capture.Batch, () =>
          capture.Inputs.Select((index, slot) =>
            new KeyValuePair<int, object>(slot, index == -1 ? obj : args[index]))
            .ToDictionary(binding => binding.Key, binding => binding.Value),
          nativeError, $"hook {eventKey.Type}.{eventKey.Name}", out captureError);
      }

      if (capture?.IncludeParameters == false)
      {
        QueueCallback(token, timestamp, captured, captureError, sequence);
      }
      else
      {
        QueueCallback(token, timestamp, captured, captureError, sequence, obj, (object)mappedArgs);
      }
    };

  private Func<object, object[], Action<Exception>> CreateHookScopeCallback(
    int token, (string Type, string Name) eventKey, RemoteHookCapture capture,
    HookCaptureFilter filter,
    Action<object, object[], RemoteCaptureSequence, Exception> completeCapture) =>
    (obj, args) =>
    {
      var scope = capture.Scope;
      if (!IsCallbackActive(token))
        return null;
      if (!GlobalEvents.IsValidEvent(eventKey, obj, args, out _))
        return null;
      if (!filter.TryMatch(obj, args, out var captureRoot))
        return null;
      int index = capture.Inputs[scope.OwnerInput];
      object owner = index == -1 ? captureRoot : args[index];
      if (scope.OwnerPath != null)
        owner = _batchExecutor.Read(owner, scope.OwnerPath);
      if (capture.Filters.Count != 0 &&
          !_captureScopes.HasSubscribers(scope.Group, owner, captureRoot))
        return null;
      var exit = _captureScopes.Enter(scope.Group, owner, capture.CaptureOnExit,
        (sequence, error) => completeCapture(captureRoot, args, sequence, error), scope.IsStateBoundary);
      if (exit != null && capture.Filters.Count != 0)
        _captureScopes.MarkChanged(scope.Group, owner, eventKey.Name);

      // CaptureOnExit delivers the initial capture that lets a client attach
      // to this owner. It has no stream sequence because registration has not
      // established a baseline yet.
      if (exit == null && capture.CaptureOnExit && capture.Filters.Count == 0 &&
          !_captureScopes.HasSubscribers(scope.Group, owner))
        return error => completeCapture(obj, args, null, error);
      return exit;
    };
}
