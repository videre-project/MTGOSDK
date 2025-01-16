/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Reflection.Extensions;


namespace ScubaDiver;

public partial class Diver : IDisposable
{

  private string MakeHookMethodResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got Hook Method request!");

    string body;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }
    if (string.IsNullOrEmpty(body))
      return QuickError("Missing body");

    var request = JsonConvert.DeserializeObject<FunctionHookRequest>(body);
    if (request == null)
      return QuickError("Failed to deserialize body");

    if (!IPAddress.TryParse(request.IP, out IPAddress ipAddress))
      return QuickError("Failed to parse IP address. Input: " + request.IP);

    int port = request.Port;
    IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
    string typeFullName = request.TypeFullName;
    string methodName = request.MethodName;
    string hookPosition = request.HookPosition;
    HarmonyPatchPosition pos = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPosition);
    if (!Enum.IsDefined(typeof(HarmonyPatchPosition), pos))
      return QuickError("hook_position has an invalid or unsupported value");

    Type resolvedType;
    lock (_runtime.clrLock)
    {
      resolvedType = _runtime.ResolveType(typeFullName);
    }
    if (resolvedType == null)
      return QuickError("Failed to resolve type");

    Type[] paramTypes;
    lock (_runtime.clrLock)
    {
      paramTypes = request.ParametersTypeFullNames.Select(typeFullName => _runtime.ResolveType(typeFullName)).ToArray();
    }

    // We might be searching for a constructor. Switch based on method name.
    MethodBase methodInfo = methodName == ".ctor"
      ? resolvedType.GetConstructor(paramTypes)
      : resolvedType.GetMethodRecursive(methodName, paramTypes);
    if (methodInfo == null)
      return QuickError($"Failed to find method {methodName} in type {resolvedType.Name}");
    Log.Debug("[Diver] Hook Method - Resolved Method");

    // See if any remoteHooks already hooked into this method (by MethodInfo object)
    var existingHook = _remoteHooks
      .FirstOrDefault(kvp => kvp.Value.OriginalHookedMethod == methodInfo);
    if (existingHook.Value != null)
    {
      Log.Debug($"[Diver] Hook Method - Found existing hook for {methodName}");
      _remoteHooks.TryRemove(existingHook.Key, out RegisteredMethodHookInfo rmhi);
      HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
      // return JsonConvert.SerializeObject(new EventRegistrationResults(){ Token = existingHook.Key });
    }

    // We're all good regarding the signature!
    // assign subscriber unique id
    int token = AssignCallbackToken();
    Log.Debug($"[Diver] Hook Method - Assigned Token: {token}");

    // Preparing a proxy method that Harmony will invoke
    HarmonyWrapper.HookCallback patchCallback = (obj, args) =>
    {
      InvokeControllerCallback(endpoint, token, new StackTrace().ToString(), obj, args);
    };

    Log.Debug($"[Diver] Hooking function {methodName}...");
    try
    {
      HarmonyWrapper.Instance.AddHook(methodInfo, pos, patchCallback);
    }
    catch (Exception ex)
    {
      // Hooking filed so we cleanup the Hook Info we inserted beforehand
      _remoteHooks.TryRemove(token, out _);
      Log.Debug($"[Diver] Failed to hook func {methodName}. Exception: {ex}");
      return QuickError("Failed insert the hook for the function. HarmonyWrapper.AddHook failed.");
    }

    Log.Debug($"[Diver] Hooked func {methodName}!");
    // Keeping all hooking information aside so we can unhook later.
    _remoteHooks[token] = new RegisteredMethodHookInfo()
    {
      Endpoint = endpoint,
      OriginalHookedMethod = methodInfo,
      RegisteredProxy = patchCallback
    };

    EventRegistrationResults erResults = new() { Token = token };

    return JsonConvert.SerializeObject(erResults);
  }
}
