/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
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
        if (captureDescription.Scope?.IsStateBoundary == true && !captureDescription.CaptureOnExit)
          throw new ArgumentException("State boundaries must capture on completion.");
        BatchExecutor.Validate(captureDescription.Batch);
        CaptureScopeManager.Validate(captureDescription.Scope, captureDescription.Batch);
        if (captureDescription.Scope != null &&
            captureDescription.Scope.OwnerInput >= captureDescription.Inputs.Count)
          throw new ArgumentException("The scope owner must be bound to a native hook parameter.");
        if (captureDescription.Scope != null && (pos != HarmonyPatchPosition.Postfix))
          throw new ArgumentException("Scoped hooks deliver completion captures at the postfix position.");
        captureDescription.Filters ??= new();
        if (captureDescription.Filters.Count != 0 && captureDescription.Scope == null)
          throw new ArgumentException("Native capture filters require a capture scope.");
        captureFilter = new HookCaptureFilter(captureDescription.Filters, methodInfo,
          name => _runtime.ResolveType(name), _batchExecutor);
      }
      catch (Exception error) { return QuickError("Invalid hook capture: " + error.Message); }
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

      // Capture before queueing delivery so the callback receives values from
      // this native invocation.
      Action<object, object[], RemoteCaptureSequence, Exception> completeCapture = (obj, args, sequence, nativeError) =>
      {
        if (!IsCallbackActive(token)) return;
        DateTime timestamp = DateTime.UtcNow;
        var eventKey = (resolvedType.FullName, methodName);
        dynamic mappedArgs = args;
        if (sequence == null && !GlobalEvents.IsValidEvent(eventKey, obj, args, out mappedArgs))
        {
          return;
        }

        RemoteBatchResult captured = null;
        RemoteBatchError captureError = null;
        if (request.Capture is { } description)
        {
          try
          {
            var inputs = description.Inputs.Select((index, slot) =>
              new { Slot = slot, Value = index == -1 ? obj : args[index] })
              .ToDictionary(binding => binding.Slot, binding => binding.Value);
            if (nativeError == null)
              captured = _batchExecutor.Execute(description.Batch, inputs);
            else
            {
              // Report failure to the subscriber without replacing the native exception.
              captureError = new RemoteBatchError
              {
                OperationId = -1,
                ExceptionType = nativeError.GetType().FullName,
                Message = nativeError.Message,
                StackTrace = nativeError.ToString()
              };
            }
          }
          catch (Exception error)
          {
            captureError = new RemoteBatchError
            {
              OperationId = -1,
              ExceptionType = error.GetType().FullName,
              Message = error.Message,
              StackTrace = error.ToString()
            };
            Log.Error($"[Diver] Hook capture failed for {resolvedType.FullName}.{methodName}: {error}");
          }
        }
        if (request.Capture?.IncludeParameters == false)
        {
          QueueCallback(token, timestamp, captured, captureError, sequence);
        }
        else
        {
          QueueCallback(token, timestamp, captured, captureError, sequence, obj, (object)mappedArgs);
        }
      };
      HarmonyWrapper.HookCallback patchCallback = (obj, args) => completeCapture(obj, args, null, null);
      registration.RegisteredProxy = patchCallback;

      Log.Debug($"[Diver] Hooking function {methodName}...");
      try
      {
        var wrapper = HarmonyWrapper.Instance;
        if (request.Capture?.Scope is { } scope)
        {
          wrapper.AddScopeHook(methodInfo, (obj, args) =>
          {
            if (!IsCallbackActive(token)) return null;
            if (!GlobalEvents.IsValidEvent((resolvedType.FullName, methodName), obj, args, out _))
              return null;
            if (!captureFilter.TryMatch(obj, args, out var captureRoot))
              return null;
            int index = request.Capture.Inputs[scope.OwnerInput];
            object owner = index == -1 ? captureRoot : args[index];
            if (scope.OwnerPath != null) owner = _batchExecutor.Read(owner, scope.OwnerPath);
            if (request.Capture.Filters.Count != 0 &&
                !_captureScopes.HasSubscribers(scope.Group, owner, captureRoot))
              return null;
            var exit = _captureScopes.Enter(scope.Group, owner, request.Capture.CaptureOnExit,
              (sequence, error) => completeCapture(captureRoot, args, sequence, error), scope.IsStateBoundary);
            if (exit != null && request.Capture.Filters.Count != 0)
              _captureScopes.MarkChanged(scope.Group, owner, methodName);
            // CaptureOnExit delivers the initial capture that lets a client attach
            // to this owner. It has no stream sequence because registration has not
            // established a baseline yet.
            if (exit == null && request.Capture.CaptureOnExit && request.Capture.Filters.Count == 0 &&
                !_captureScopes.HasSubscribers(scope.Group, owner))
              return error => completeCapture(obj, args, null, error);
            return exit;
          });
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

      var erResults = new EventRegistrationResults { Token = token };
      return WrapSuccess(erResults);
    }
  }
}
