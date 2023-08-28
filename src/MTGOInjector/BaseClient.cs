/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Reflection;

using RemoteNET;


namespace MTGOInjector;

public class BaseClient
{
  
  /// <summary>
  /// The native process handle to the client.
  /// </summary>
  protected virtual Process ClientProcess { get; private set; } = default!;

  /// <summary>
  /// The RemoteNET handle to interact with the client.
  /// </summary>
  public readonly RemoteApp Client;

  /// <summary>
  /// A list of non-system modules loaded by the client.
  /// </summary>
  public IEnumerable<ProcessModule> ClientModules =>
    ClientProcess.Modules
      .Cast<ProcessModule>()
      .Where(m =>
        new string[] { "\\Windows\\", "\\ProgramData\\" }
          .All(s => m.FileName.Contains(s) == false));

  /// <summary>
  /// The directory path to extract runtime injector and diver assemblies to.
  /// </summary>
  protected virtual string ExtractDir { get; private set; } = "";

  /// <summary>
  /// Indicates whether the client has reconnected to the diver.
  /// </summary>
  public bool Is_Reconnect { get; private set; } = false;

  public BaseClient()
  {
    Bootstrapper.ExtractDir = ExtractDir;
    Client = GetClientHandle();
  }

  /// <summary>
  /// Connects to the target process and returns a RemoteNET client handle.
  /// </summary>
  private RemoteApp GetClientHandle()
  {
    // Check if the client injector is already loaded
    Is_Reconnect = ClientModules
      .Any(m => m.FileName.Contains("Bootstrapper"));

    // Connect to the target process
    var Client = RemoteApp.Connect(ClientProcess);

    // Verify that the injected assembly is loaded and reponding
    if (Client.Communicator.CheckAliveness() is false)
      throw new Exception("RemoteNET Diver is not responding to requests.");

    return Client;
  }

  /// <summary>
  /// Disconnects from the target process and disposes of the client handle.
  /// </summary>
  public virtual void Dispose()
  {
    Client.Dispose();
    ClientProcess.Kill();
  }

  //
  // ManagedRemoteApp wrapper methods
  //

  public dynamic GetInstance(string queryPath)
  {
    return GetInstances(queryPath).Single();
  }

  public IEnumerable<dynamic> GetInstances(string queryPath)
  {
    IEnumerable<CandidateObject> queryRefs = Client.QueryInstances(queryPath);
    foreach (var candidate in queryRefs)
    {
      var queryObject = Client.GetRemoteObject(candidate);
      yield return queryObject.Dynamify();
    }
  }

  public Type GetInstanceType(string queryPath)
  {
    return GetInstanceTypes(queryPath).Single();
  }

  public IEnumerable<Type> GetInstanceTypes(string queryPath)
  {
    IEnumerable<CandidateType> queryRefs = Client.QueryTypes(queryPath);
    foreach (var candidate in queryRefs)
    {
      var queryObject = Client.GetRemoteType(candidate);
      yield return queryObject;
    }
  }

  public MethodInfo GetInstanceMethod(string queryPath, string methodName)
  {
    return GetInstanceMethods(queryPath, methodName).Single();
  }

  public IEnumerable<MethodInfo> GetInstanceMethods(string queryPath,
                                                    string methodName)
  {
    Type type = GetInstanceType(queryPath);
    var methods = type.GetMethods((BindingFlags)0xffff)
      .Where(mInfo => mInfo.Name == methodName);

    return methods;
  }

  public dynamic CreateInstance(string queryPath, params object[] parameters)
  {
    RemoteActivator activator = Client.Activator;
    RemoteObject queryObject = activator.CreateInstance(queryPath, parameters);
    return queryObject.Dynamify();
  }

  //
  // Reflection wrapper methods
  //

  public MethodInfo? GetMethod(string queryPath,
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
  public dynamic InvokeMethod(string queryPath,
                              string methodName,
                              Type[]? genericTypes=null,
                              params object[]? args)
  {
    var remoteMethod = GetMethod(queryPath, methodName, genericTypes);
#pragma warning disable CS8603
    return remoteMethod!.Invoke(null, args);
#pragma warning restore CS8603
  }

  //
  // HookingManager wrapper methods
  //

  public void HookInstanceMethod(string queryPath,
                                 string methodName,
                                 string hookName,
                                 HookAction callback)
  {
    MethodInfo method = GetInstanceMethod(queryPath, methodName);
    switch (hookName)
    {
      //
      // FIXME: prefix/postfix patches break on subsequent client connections.
      //
      case "prefix":
        Client.Harmony.Patch(method, prefix: callback);
        break;
      case "postfix":
        Client.Harmony.Patch(method, postfix: callback);
        break;
      case "finalizer":
        Client.Harmony.Patch(method, finalizer: callback);
        break;
      default:
        throw new Exception($"Unknown hook type: {hookName}");
    }
  }

  // TODO: Add unhooking methods + unhook all methods on exit
  //       (or just unhook all methods on exit)

  // public void UnhookInstanceMethod(string queryPath,
  //                                  string methodName,
  //                                  string hookName)
}
