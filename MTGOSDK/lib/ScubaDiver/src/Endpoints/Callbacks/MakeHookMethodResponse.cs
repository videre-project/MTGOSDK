/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

using MessagePack;

using MTGOSDK;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeHookMethodResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got Hook Method request!");

    var body = ReadRequestBody(arg);
    if (body == null || body.Length == 0)
      return QuickError("Missing body");

    FunctionHookRequest request;
    try
    {
      request = MessagePackSerializer.Deserialize<FunctionHookRequest>(body);
    }
    catch
    {
      return QuickError("Failed to deserialize body");
    }

    if (request == null)
      return QuickError("Failed to deserialize body");

    if (!IPAddress.TryParse(request.IP, out IPAddress ipAddress))
      return QuickError("Failed to parse IP address. Input: " + request.IP);

    int port = request.Port;
    IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
    string typeFullName = request.TypeFullName;
    string methodName = request.MethodName;
    string hookPosition = request.HookPosition;
    HarmonyPatchPosition pos = (HarmonyPatchPosition) Enum.Parse(typeof(HarmonyPatchPosition), hookPosition);
    if (!Enum.IsDefined(typeof(HarmonyPatchPosition), pos))
      return QuickError("hook_position has an invalid or unsupported value");

    Type resolvedType;
    lock (_runtime.clrLock)
    {
      resolvedType = _runtime.ResolveType(typeFullName);
    }
    if (resolvedType == null)
      return QuickError("Failed to resolve type");

    int paramCount = request.ParametersTypeFullNames?.Count ?? 0;
    Type[] paramTypes = new Type[paramCount];
    lock (_runtime.clrLock)
    {
      for (int i = 0; i < paramCount; i++)
      {
        paramTypes[i] = _runtime.ResolveType(request.ParametersTypeFullNames[i]);
      }
    }

    MethodBase methodInfo = methodName == ".ctor"
      ? resolvedType.GetConstructor(paramTypes)
      : resolvedType.GetMethodRecursive(methodName, paramTypes);
    if (methodInfo == null)
      return QuickError($"Failed to find method {methodName} in type {resolvedType.Name}");

    Log.Debug($"[Diver] Hook Method - Resolved Method {methodInfo.Name}");

    var existingHook = _remoteHooks
      .FirstOrDefault(kvp => kvp.Value.OriginalHookedMethod == methodInfo);
    string uniqueId = HarmonyWrapper.GetUniqueId(typeFullName, methodName);
    bool hasCallback = HarmonyWrapper.HasCallback(uniqueId);
    if (existingHook.Value != null || hasCallback)
    {
      Log.Debug($"[Diver] Hook Method - Found existing hook for {methodName}");
      if (_remoteHooks.TryGetValue(existingHook.Key, out RegisteredMethodHookInfo rmhi))
      {
        if (rmhi.Endpoint.Equals(endpoint))
        {
          Log.Debug("[Diver] Hook Method - Endpoint is the same, not re-hooking");
          var result = new EventRegistrationResults { Token = existingHook.Key };
          return WrapSuccess(result);
        }

        bool portInUse = true;
        try
        {
          using var tcpClient = new TcpClient();
          tcpClient.Connect(rmhi.Endpoint);
          portInUse = false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
          portInUse = false;
        }
        catch (Exception ex)
        {
          portInUse = false;
          Log.Debug($"[Diver] Hook Method - Failed to connect to existing endpoint {rmhi.Endpoint}. Exception: {ex}");
        }

        Log.Debug($"[Diver] Hook Method - Port in use: {portInUse}");
        if (!portInUse)
        {
          Log.Debug($"[Diver] Hook Method - Removing old hook for {methodName}");
          _remoteHooks.TryRemove(existingHook.Key, out _);
          HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
        }
      }
      {
        Log.Debug($"[Diver] Hook Method - Method {methodName} is already hooked, but no endpoint is associated with it.");
        Log.Debug($"[Diver] Hook Method - Removing old hook for {methodName}");
        HarmonyWrapper.Instance.RemovePrefix(methodInfo);
      }
    }

    int token = AssignCallbackToken();
    _callbackTokens[token] = new CancellationTokenSource();
    Log.Debug($"[Diver] Hook Method - Assigned Token: {token}");

    int clientPort = arg.RemoteEndPoint.Port;
    lock (_registeredPidsLock)
    {
      if (_clientCallbacks.TryGetValue(clientPort, out var tokens))
        tokens.Add(token);
    }

    HarmonyWrapper.HookCallback patchCallback = (obj, args) =>
    {
      DateTime timestamp = DateTime.Now;
      var eventKey = (resolvedType.FullName, methodName);
      if (!GlobalEvents.IsValidEvent(eventKey, obj, args, out var mappedArgs)) return;

      _ = SyncThread.EnqueueAsync(
        async () => await InvokeControllerCallback(endpoint, token, timestamp, obj, mappedArgs),
        true,
        TimeSpan.FromSeconds(5));
    };

    Log.Debug($"[Diver] Hooking function {methodName}...");
    try
    {
      HarmonyWrapper.Instance.AddHook(methodInfo, pos, patchCallback);
    }
    catch (Exception ex)
    {
      _remoteHooks.TryRemove(token, out _);
      Log.Debug($"[Diver] Failed to hook func {methodName}. Exception: {ex}");
      return QuickError("Failed insert the hook for the function. HarmonyWrapper.AddHook failed.");
    }

    Log.Debug($"[Diver] Hooked func {methodName}!");
    _remoteHooks[token] = new RegisteredMethodHookInfo()
    {
      Endpoint = endpoint,
      OriginalHookedMethod = methodInfo,
      RegisteredProxy = patchCallback
    };

    var erResults = new EventRegistrationResults { Token = token };
    return WrapSuccess(erResults);
  }
}
