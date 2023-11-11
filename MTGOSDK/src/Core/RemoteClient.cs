/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.IO;
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
  /// <returns>A RemoteNET client handle.</returns>
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

  /// <summary>
  /// Returns a single instance of a remote object from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <returns>A dynamic wrapper around the remote object.</returns>
  public static dynamic GetInstance(string queryPath)
  {
    return GetInstances(queryPath).Single();
  }

  /// <summary>
  /// Returns a collection of remote objects from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote objects.</param>
  /// <returns>A collection of dynamic wrappers around the remote objects.</returns>
  public static IEnumerable<dynamic> GetInstances(string queryPath)
  {
    IEnumerable<CandidateObject> queryRefs = @client.QueryInstances(queryPath);
    foreach (var candidate in queryRefs)
    {
      var queryObject = @client.GetRemoteObject(candidate);
      yield return queryObject.Dynamify();
    }
  }

  /// <summary>
  /// Returns a single instance of a remote type from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote type.</param>
  /// <returns>A dynamic wrapper around the remote type.</returns>
  public static Type GetInstanceType(string queryPath)
  {
    return GetInstanceTypes(queryPath).Single();
  }

  /// <summary>
  /// Returns a collection of remote types from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote types.</param>
  /// <returns>A collection of dynamic wrappers around the remote types.</returns>
  public static IEnumerable<Type> GetInstanceTypes(string queryPath)
  {
    IEnumerable<CandidateType> queryRefs = @client.QueryTypes(queryPath);
    foreach (var candidate in queryRefs)
    {
      var queryObject = @client.GetRemoteType(candidate);
      yield return queryObject;
    }
  }

  /// <summary>
  /// Returns a MethodInfo object for a given remote object's method.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="methodName">The name of the method to get.</param>
  /// <returns>A MethodInfo object for the given method.</returns>
  public static MethodInfo GetInstanceMethod(
    string queryPath,
    string methodName)
  {
    return GetInstanceMethods(queryPath, methodName).Single();
  }

  /// <summary>
  /// Returns a collection of MethodInfo objects for a given remote object's methods.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="methodName">The name of the method to get.</param>
  /// <returns>A collection of MethodInfo objects for the given methods.</returns>
  public static IEnumerable<MethodInfo> GetInstanceMethods(
    string queryPath,
    string methodName)
  {
    Type type = GetInstanceType(queryPath);
    var methods = type.GetMethods((BindingFlags)0xffff)
      .Where(mInfo => mInfo.Name == methodName);

    return methods;
  }

  /// <summary>
  /// Creates a new instance of a remote object from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="parameters">The parameters to pass to the remote object's constructor.</param>
  /// <returns>A dynamic wrapper around the remote object.</returns>
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

  /// <summary>
  /// Returns a MethodInfo object for a given remote type's method.
  /// </summary>
  /// <param name="queryPath">The query path to the remote type.</param>
  /// <param name="methodName">The name of the method to get.</param>
  /// <param name="genericTypes">The generic types to fill in.</param>
  /// <returns>A MethodInfo object for the given method.</returns>
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
  /// <param name="queryPath">The query path to the remote type.</param>
  /// <param name="methodName">The name of the method to invoke.</param>
  /// <param name="genericTypes">The generic types to fill in.</param>
  /// <param name="args">The arguments to pass to the method.</param>
  /// <returns>The return value of the method.</returns>
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

  /// <summary>
  /// Hooks a remote object's method using a Harmony callback.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="methodName">The name of the method to hook.</param>
  /// <param name="hookName">The type of Harmony hook to use.</param>
  /// <param name="callback">The local Harmony callback to use.</param>
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
