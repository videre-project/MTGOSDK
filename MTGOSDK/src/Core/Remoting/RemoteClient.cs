/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.IO;
using System.Reflection;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Types;
using MTGOSDK.Core.Remoting.Structs;
using MTGOSDK.Resources;

using MTGOSDK.Win32.API;
using MTGOSDK.Win32.Utilities;


namespace MTGOSDK.Core.Remoting;
using static MTGOSDK.Win32.Constants;

/// <summary>
/// A singleton class that manages the connection to the MTGO client process.
/// </summary>
public sealed class RemoteClient : DLRWrapper<dynamic>
{
  //
  // Singleton instance and static accessors
  //

  private static Lazy<RemoteClient> s_instance = new(() => new RemoteClient());
  public static RemoteClient @this => s_instance.Value;
  public static RemoteHandle @client => @this._clientHandle;
  public static Process @process => @this._clientProcess;

  /// <summary>
  /// Whether the RemoteClient singleton has been initialized.
  /// </summary>
  public static bool IsInitialized => s_instance.IsValueCreated;

  /// <summary>
  /// The directory path to extract runtime injector and diver assemblies to.
  /// </summary>
  public static string ExtractDir =
    Path.Combine(/* %LocalAppData%\ */ "MTGOSDK", "bin");

  /// <summary>
  /// Whether to destroy the MTGO process when disposing of the Remote Client.
  /// </summary>
  public static bool CloseOnExit = false;

  /// <summary>
  /// The port to manage the connection to the MTGO client process.
  /// </summary>
  public static ushort? Port = null;

  private RemoteClient()
  {
    Bootstrapper.ExtractDir = ExtractDir;
    _clientHandle = GetClientHandle();
  }

  /// <summary>
  /// Ensures that the RemoteClient singleton is initialized.
  /// </summary>
  public static void EnsureInitialize()
  {
    // Manually initialize the singleton instance if not already created
    if (!IsInitialized) _ = s_instance.Value;
  }

  //
  // Process helper and automation methods
  //

  /// <summary>
  /// The ClickOnce deployment process.
  /// </summary>
  private static Process ClickOnceProcess() =>
    Process.GetProcessesByName("dfsvc")
      .OrderBy(x => x.StartTime)
      .LastOrDefault();

  /// <summary>
  /// Fetches the MTGO client process.
  /// </summary>
  private static Process MTGOProcess() =>
    Process.GetProcessesByName("MTGO")
      .OrderBy(x => x.StartTime)
      .FirstOrDefault();

  /// <summary>
  /// Whether the MTGO (ClickOnce) deployment has started.
  /// </summary>
  public static bool IsStarting =>
    Try<bool>(() =>
      ClickOnceProcess().MainWindowTitle.Contains("Launching Process"));

  /// <summary>
  /// Whether ClickOnce is currently updating the MTGO client.
  /// </summary>
  public static bool IsUpdating =>
    Try<bool>(() =>
      ClickOnceProcess().MainWindowTitle.Contains("Magic The Gathering Online"));

  /// <summary>
  /// Whether the MTGO client process has started.
  /// </summary>
  public static bool HasStarted => MTGOProcess() is not null;

  /// <summary>
  /// Starts the MTGO client process.
  /// </summary>
  /// <remarks>
  /// Note: This method will close any existing MTGO processes when called.
  /// <para/>
  /// If no MTGO installation exists or is out of date, this method will
  /// attempt to install or update the client before starting it.
  /// </remarks>
  /// <returns>True if the MTGO client process was started.</returns>
  /// <exception cref="SetupFailureException">
  /// Thrown when the MTGO installation has failed.
  /// </exception>
  /// <exception cref="ExternalErrorException">
  /// Thrown when the MTGO process failed to start.
  /// </exception>
  public static async Task<bool> StartProcess()
  {
    // Close any existing MTGO processes.
    try { using var p = MTGOProcess(); p.Kill(); p.WaitForExit(); } catch { }

    // Start MTGO using the ClickOnce application manifest uri.
    Log.Debug("Starting MTGO process as the current desktop user.");
    try
    {
      //
      // This makes a call to CreateProcessAsUser from the Win32 API to start the
      // process as the current user. This requires the SeIncreaseQuotaPrivilege
      // (and potentially SE_ASSIGNPRIMARYTOKEN_NAME if not assignable).
      //
      // This requires enabling 'Replace a process level token' for the user
      // account running this process. Refer to the below resource for more info:
      // https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/replace-a-process-level-token
      //
      using var process = ProcessUtilities.RunAsDesktopUser(
        "rundll32.exe",
        $"dfshim.dll,ShOpenVerbApplication {ApplicationUri}"
      );
      process.WaitForExit();
    }
    catch
    {
      //
      // Fall back to starting the process assuming the process has proper
      // permissions set to start as the current user (or is now set).
      //
      using var process = new Process();
      process.StartInfo = new ProcessStartInfo()
      {
        FileName = "rundll32.exe",
        Arguments = $"dfshim.dll,ShOpenVerbApplication {ApplicationUri}",
      };
      process.Start();
      process.WaitForExit();
    }

    //
    // Check for ClickOnce installation or updates and wait for it to finish.
    //
    // This will wait up to 5 seconds for the ClickOnce process to start, then
    // fallback and wait up to 5 seconds for ClickOnce to begin updating.
    //
    // If the MTGO process hasn't started after the first check or 5 minutes
    // after updating, the process is considered to have failed startup.
    //
    if ((await WaitUntil(() => IsStarting, delay:  250, retries: 20 )) ||
        (await WaitUntil(() => IsUpdating, delay:  250, retries: 20 )) &&
       !(await WaitUntil(() => HasStarted, delay: 5000, retries: 60 )))
    {
      throw new SetupFailureException("The MTGO installation has failed.");
    }

    //
    // Here we perform another check to verify that the process has stalled
    // during installation or updating to better inform the user.
    //
    if (!(await WaitUntil(() => HasStarted)) && (IsStarting || IsUpdating))
    {
      throw new SetupFailureException(
          "The MTGO installation stalled and did not finish.");
    }
    else if (!HasStarted)
    {
      throw new ExternalErrorException("The MTGO process failed to start.");
    }

    // Wait for the MTGO process UI to start and open kicker window.
    return await WaitUntil(() => MTGOProcess().MainWindowHandle != IntPtr.Zero);
  }

  public static bool MinimizeWindow()
  {
    var handle = MTGOProcess().MainWindowHandle;
    bool hr = User32.ShowWindow(handle, ShowWindowFlags.SW_MINIMIZE);
    Log.Debug("Minimized MTGO window");

    return hr;
  }

  //
  // Process and RemoteNET state management
  //

  /// <summary>
  /// The RemoteNET handle to interact with the client.
  /// </summary>
  private readonly RemoteHandle _clientHandle;

  /// <summary>
  /// The native process handle to the MTGO client.
  /// </summary>
  private readonly Process _clientProcess =
    MTGOProcess()
      ?? throw new NullReferenceException("MTGO client process not found.");

  /// <summary>
  /// Connects to the target process and returns a RemoteNET client handle.
  /// </summary>
  /// <returns>A RemoteNET client handle.</returns>
  private RemoteHandle GetClientHandle()
  {
    // Connect to the MTGO process
    ushort port = Port ??= Cast<ushort>(_clientProcess.Id);
    Log.Trace("Connecting to MTGO process on port {Port}", port);
    RemoteHandle client = RemoteHandle.Connect(_clientProcess, port);

    // Teardown on fatal exception.
    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
    {
      if (!IsInitialized)
      {
        Log.Critical("Encountered a fatal exception. Cleaning up RemoteClient.");
        Dispose();
      }
    };

    // Verify that the injected assembly is loaded and reponding
    if (client.Communicator.CheckAliveness() is false)
      throw new TimeoutException("Diver is not responding to requests.");
    else
      Log.Debug("Established a connection to the MTGO process.");

    return client;
  }

  /// <summary>
  /// Disconnects from the target process and disposes of the client handle.
  /// </summary>
  internal static void Dispose()
  {
    // Prevent multiple calls to Dispose
    if (!IsInitialized) return;

    // Call all event subscribers first before disposing
    Log.Debug("Disposing RemoteClient.");
    Disposed?.Invoke(null, EventArgs.Empty);
    Disposed = null;

    // Cleanup all resources and dispose of the client handle
    @client.Dispose();
    Port = null;

    // Kill the MTGO process if CloseOnExit is set
    if (CloseOnExit)
    {
      @process.Kill();
      CloseOnExit = false;
    }

    // Reset the singleton instance to allow lazy reinitialization
    s_instance = new Lazy<RemoteClient>(() => new RemoteClient());
    Log.Trace("RemoteClient disposed.");
  }

  /// <summary>
  /// Event raised when the RemoteClient is disposed.
  /// </summary>
  public static event EventHandler? Disposed;

  //
  // RemoteHandle wrapper methods
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
    try
    {
      var remoteType = GetInstanceType(queryPath);
      var remoteMethod = remoteType.GetMethod(methodName);

      // Fills in a generic method if generic types are specified
      if (genericTypes is not null)
        return remoteMethod!.MakeGenericMethod(genericTypes);

      return remoteMethod;
    }
    catch (InvalidOperationException)
    {
      throw new MissingMethodException(
          $"Method '{methodName}' not found on remote type '{queryPath}'.");
    }
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
    return remoteMethod!.Invoke(null, args);
  }
}
