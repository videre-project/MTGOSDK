/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Reflection;

using RemoteNET;


namespace MTGOSDK.Core;

/// <summary>
/// A singleton class that manages the connection to the MTGO client process.
/// </summary>
public sealed class RemoteClient
{
  //
  // Singleton instance and static accessors
  //

  private static readonly Lazy<RemoteClient> s_instance = new(() => new RemoteClient());
  public static RemoteClient @this => s_instance.Value;
  public static RemoteApp @client => @this._clientHandle;
  public static Process @process => @this._clientProcess;

  /// <summary>
  /// The directory path to extract runtime injector and diver assemblies to.
  /// </summary>
  public static string ExtractDir =
    Path.Join(/* %appdata%\..\Local\ */ "MTGOSDK", "MTGOInjector", "bin");

  private RemoteClient()
  {
    Bootstrapper.ExtractDir = ExtractDir;
    _clientHandle = GetClientHandle();
  }

  //
  // Process and RemoteNET state management
  //

  /// <summary>
  /// The RemoteNET handle to interact with the client.
  /// </summary>
  private readonly RemoteApp _clientHandle;

  /// <summary>
  /// The native process handle to the MTGO client.
  /// </summary>
  private readonly Process _clientProcess =
    Process.GetProcessesByName("MTGO")
      .OrderBy(x => x.StartTime)
      .FirstOrDefault()
        ?? throw new Exception("MTGO process not found.");

  /// <summary>
  /// Connects to the target process and returns a RemoteNET client handle.
  /// </summary>
  private RemoteApp GetClientHandle()
  {
    // Connect to the target process
    var client = RemoteApp.Connect(_clientProcess);

    // Verify that the injected assembly is loaded and reponding
    if (client.Communicator.CheckAliveness() is false)
      throw new Exception("RemoteNET Diver is not responding to requests.");

    return client;
  }

  /// <summary>
  /// Disconnects from the target process and disposes of the client handle.
  /// </summary>
  ~RemoteClient()
  {
    @client.Dispose();
    @process.Kill();
  }

  //
  // RemoteApp wrapper methods
  //

  public static dynamic GetInstance(string queryPath)
  {
    return GetInstances(queryPath).Single();
  }

  public static IEnumerable<dynamic> GetInstances(string queryPath)
  {
    IEnumerable<CandidateObject> queryRefs = @client.QueryInstances(queryPath);
    foreach (var candidate in queryRefs)
    {
      var queryObject = @client.GetRemoteObject(candidate);
      yield return queryObject.Dynamify();
    }
  }

  public static Type GetInstanceType(string queryPath)
  {
    return GetInstanceTypes(queryPath).Single();
  }

  public static IEnumerable<Type> GetInstanceTypes(string queryPath)
  {
    IEnumerable<CandidateType> queryRefs = @client.QueryTypes(queryPath);
    foreach (var candidate in queryRefs)
    {
      var queryObject = @client.GetRemoteType(candidate);
      yield return queryObject;
    }
  }

  public static MethodInfo GetInstanceMethod(
    string queryPath,
    string methodName)
  {
    return GetInstanceMethods(queryPath, methodName).Single();
  }

  public static IEnumerable<MethodInfo> GetInstanceMethods(
    string queryPath,
    string methodName)
  {
    Type type = GetInstanceType(queryPath);
    var methods = type.GetMethods((BindingFlags)0xffff)
      .Where(mInfo => mInfo.Name == methodName);

    return methods;
  }

  public static dynamic CreateInstance(
    string queryPath,
    params object[] parameters)
  {
    RemoteActivator activator = @client.Activator;
    RemoteObject queryObject = activator.CreateInstance(queryPath, parameters);
    return queryObject.Dynamify();
  }

  //
  // Reflection wrapper methods
  //

  public static MethodInfo? GetMethod(
    string queryPath,
    string methodName,
    Type[]? genericTypes=null)
  {
    var remoteType = GetInstanceType(queryPath);
    var remoteMethod = remoteType.GetMethod(methodName);

    // Fills in a generic method if generic types are specified
    if (genericTypes is not null)
      return remoteMethod!.MakeGenericMethod(genericTypes);

    return remoteMethod;
  }

  /// <summary>
  /// Invokes a static method on the target process.
  /// </summary>
  public static dynamic InvokeMethod(
    string queryPath,
    string methodName,
    Type[]? genericTypes=null,
    params object[]? args)
  {
    var remoteMethod = GetMethod(queryPath, methodName, genericTypes);
#pragma warning disable CS8603 // Possible null reference return.
    return remoteMethod!.Invoke(null, args);
#pragma warning restore CS8603
  }

  //
  // HarmonyManager wrapper methods
  //

  public static void HookInstanceMethod(
    string queryPath,
    string methodName,
    string hookName,
    HookAction callback)
  {
    MethodInfo method = GetInstanceMethod(queryPath, methodName);
    switch (hookName)
    {
      case "prefix":
        @client.Harmony.Patch(method, prefix: callback);
        break;
      case "postfix":
        @client.Harmony.Patch(method, postfix: callback);
        break;
      case "finalizer":
        @client.Harmony.Patch(method, finalizer: callback);
        break;
      default:
        throw new Exception($"Unknown hook type: {hookName}");
    }
  }

  // TODO: Add unhooking methods + unhook all methods on exit
  //       (or just unhook all methods on exit)

  // public static void UnhookInstanceMethod(
  //   string queryPath,
  //   string methodName,
  //   string hookName)
}
