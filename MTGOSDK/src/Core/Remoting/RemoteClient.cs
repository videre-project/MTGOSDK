/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Types;
using MTGOSDK.Core.Remoting.Structs;
using MTGOSDK.Resources;

using MTGOSDK.Win32.API;
using MTGOSDK.Win32.Extensions;
using MTGOSDK.Win32.Utilities;
using MTGOSDK.Win32.Deployment;


namespace MTGOSDK.Core.Remoting;
using static MTGOSDK.Win32.Constants;
using static MTGOSDK.Resources.EmbeddedResources;

/// <summary>
/// A singleton class that manages the connection to the MTGO client process.
/// </summary>
public sealed class RemoteClient : DLRWrapper
{
  //
  // Singleton instance and static accessors
  //

  private static Lazy<RemoteClient> s_instance = new(() => new RemoteClient());
  private static bool _isDisposing = false;
  private static bool _isDisposed = false;
  private static readonly object _disposeLock = new();

  public static RemoteClient @this => s_instance.Value;
  public static RemoteHandle @client => @this._clientHandle;

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
  /// The native process handle to the MTGO client.
  /// </summary>
  public static Process ClientProcess = null!;

  /// <summary>
  /// The port to manage the connection to the MTGO client process.
  /// </summary>
  public static ushort? Port = null;

  private RemoteClient()
  {
    // A new instance implies the previous one (if any) is either not created
    // or has been fully disposed. Reset disposal state here.
    _isDisposed = false;
    _isDisposing = false;

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
  /// Fetches the MTGO client process.
  /// </summary>
  /// <param name="throwOnFailure">Whether to throw an exception if the process is not found.</param>
  /// <returns>The MTGO client process, or null if not found.</returns>
  /// <exception cref="ExternalErrorException">Thrown if no processes can be queried by the OS and throwOnFailure is true.</exception>
  /// <exception cref="NullReferenceException">Thrown if the current MTGO process cannot be found and throwOnFailure is true.</exception>
  public static Process? MTGOProcess(bool throwOnFailure = false)
  {
    // If we already have a ClientProcess defined, return it.
    if (!(_isDisposing || _isDisposed) && ClientProcess != null)
      return ClientProcess;

    //
    // Use the Restart Manager API to retrieve a list of processes that have or
    // were locking the MTGO executable path. This includes the MTGO process
    // itself, as well as any other previous instances that may have been
    // terminated abruptly.
    //
    // MTGOAppDirectory will always point to the most recent launch directory,
    // as it sorts all ClickOnce application directories by their creation time.
    //
    string executablePath = Path.Combine(MTGOAppDirectory, "MTGO.exe");
    var processList = new FileInfo(executablePath).GetLockingProcesses();

    // If no processes were found, we want to indicate that our syscalls failed.
    if (processList.Count == 0 && throwOnFailure)
      throw new ExternalErrorException("Unable to retrieve MTGO process.");

    var process = Try(() =>
      processList
        // Filter out processes without a valid thread count or start time.
        .Where(p =>
          Try<bool>(() =>
            p.Threads.Count > 0 &&
            p.StartTime != DateTime.MinValue))
        .OrderByDescending(p => p.StartTime)
        .FirstOrDefault(),
      fallback: null);

    if (process == null && throwOnFailure)
      throw new NullReferenceException("MTGO client process not found.");

    return process;
  }

  /// <summary>
  /// Whether the MTGO client process has started.
  /// </summary>
  public static bool HasStarted => MTGOProcess() is not null;

  /// <summary>
  /// Starts a shell process and logs the output.
  /// </summary>
  /// <param name="path">The path to the executable.</param>
  /// <param name="args">The arguments to pass to the executable.</param>
  /// <param name="timeout">The timeout to wait for the process to exit.</param>
  private static async Task StartShellProcess(
    string path,
    string args = "",
    TimeSpan? timeout = null)
  {
    using var process = new Process()
    {
      StartInfo = new ProcessStartInfo()
      {
        FileName = path,
        Arguments = args,
        UseShellExecute = true,
        CreateNoWindow = false,
        WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
      },
    };
    process.Start();

    // Return early if no timeout is specified
    if (timeout == TimeSpan.Zero) return;

    // Wait for the process to exit or timeout
    var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
    try
    {
      await process.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
      throw new ExternalErrorException($"The process '{path}' timed out after {cts.Token} seconds.");
    }
  }

  /// <summary>
  /// Installs or updates the MTGO client.
  /// </summary>
  public static async Task InstallOrUpdate()
  {
    byte[] launcherResource = GetBinaryResource(@"Resources\Launcher.exe");
    string launcherPath = Path.Combine(Bootstrapper.AppDataDir, "Launcher.exe");
    OverrideFileIfChanged(launcherPath, launcherResource);

    // Check if there are any updates first before starting MTGO.
    // This will also create a new MTGO installation if one does not exist.
    await StartShellProcess(launcherPath, ApplicationUri,
                            timeout: TimeSpan.FromMinutes(5));

    //
    // Its possible for the ClickOnce service to not be running after a new
    // installation of MTGO (usually on Windows Server i.e. Datacenter builds).
    //
    // This is a workaround to ensure that the ClickOnce service is running
    // before trying to start the MTGO process (which will fail if not running).
    //
    if (Process.GetProcessesByName("dfsvc").Length == 0)
    {
      Log.Debug("The ClickOnce service is not running. Starting it now.");
      await StartShellProcess(ClickOncePaths.ClickOnceServiceExecutable,
                              // Do not wait for the service to exit.
                              timeout: TimeSpan.Zero);
    }
  }

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
  public static async Task StartProcess()
  {
    // Check if there are any updates first before starting MTGO.
    await InstallOrUpdate();

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
      using var process = ProcessUtilities.RunAsDesktopUser(AppRefPath, "");
      await process.WaitForExitAsync();
    }
    catch
    {
      //
      // Fall back to starting the process assuming the process has proper
      // permissions set to start as the current user (or is now set).
      //
      await StartShellProcess(AppRefPath, timeout: TimeSpan.FromSeconds(10));
    }

    //
    // Here we perform another check to verify that the process has stalled
    // during installation or updating to better inform the user.
    //
    if (!await WaitUntil(() => HasStarted, delay: 500, retries: 20)) // 10s
    {
      throw new ExternalErrorException("The MTGO process failed to start.");
    }

    // Wait for the MTGO process UI to start and open kicker window.
    MTGOProcess().WaitForInputIdle();
    await WaitUntil(() => !string.IsNullOrEmpty(MTGOProcess().MainWindowTitle));
    if (!await WaitUntil(() => MTGOProcess().MainWindowHandle != IntPtr.Zero))
    {
      // Check to see if the MTGO process has any windows associated with it.
      if (!Environment.UserInteractive)
        throw new ExternalErrorException(
          "Could not launch MTGO in user interactive mode.");

      //
      // Otherwise, we'll assume that MTGO was launched using native Win32 APIs
      // which may not be reflected in the HWND returned from COM APIs. This is
      // after 10 seconds of waiting for the process's HWND to be refreshed.
      //
      // The best we can do in this case is simply check if the window title is
      // unset to indicate that the application entrypoint has not finished.
      //
      if (!string.IsNullOrEmpty(Try(() => MTGOProcess().MainWindowTitle)))
        throw new SetupFailureException("The MTGO process failed to initialize.");
    }
  }

  /// <summary>
  /// Kills the MTGO process.
  /// </summary>
  /// <returns>True if the MTGO process was killed.</returns>
  public static bool KillProcess()
  {
    var process = MTGOProcess();
    if (process is not null)
    {
      try
      {
        process.Kill();
        process.WaitForExit();
        Log.Debug("Killed MTGO process with PID {PID}.", process.Id);

        return true;
      }
      catch (Win32Exception)
      {
        process.CloseMainWindow();
        process.WaitForExit();

        process.Refresh();
        return process.HasExited;
      }

    }

    return false;
  }

  public static bool MinimizeWindow()
  {
    var handle = MTGOProcess().MainWindowHandle;
    bool hr = User32.ShowWindow(handle, ShowWindowFlags.SW_MINIMIZE);
    Log.Debug("Minimized MTGO window");

    return hr;
  }

  public static bool FocusWindow()
  {
    var handle = MTGOProcess().MainWindowHandle;
    bool hr = User32.SetForegroundWindow(handle);
    Log.Debug("Focused MTGO window");

    return hr;
  }

  //
  // Process and RemoteNET state management
  //

  /// <summary>
  /// The RemoteNET handle to interact with the client.
  /// </summary>
  private readonly RemoteHandle _clientHandle;

  // Cancellation source used for operations tied to this RemoteClient instance.
  // Not readonly so we can swap it during disposal to avoid races with late
  // callbacks (swap pattern).
  private CancellationTokenSource _cts = new();

  /// <summary>
  /// Connects to the target process and returns a RemoteNET client handle.
  /// </summary>
  /// <returns>A RemoteNET client handle.</returns>
  private RemoteHandle GetClientHandle()
  {
    bool _processHandleOverride = true;
    void RefreshClientProcess(bool throwOnFailure = false)
    {
      ClientProcess = Retry(() => MTGOProcess(true),
                            delay: 500, retries: 10, raise: throwOnFailure);
      _processHandleOverride = false;
    }

    if (ClientProcess is null) RefreshClientProcess(throwOnFailure: true);

    // Connect to the MTGO process using the specified or default port
    if (!Port.HasValue) Port = Cast<ushort>(ClientProcess.Id);
    Log.Trace("Connecting to MTGO process on port {Port}", Port.Value);

    // Suppress expected transient timeouts / connection failures while the
    // remote process is still spinning up under heavy CPU load.
    RemoteHandle handle;
    using (Log.Suppress())
    {
      handle = Retry(delegate
      {
        try
        {
          return RemoteHandle.Connect(ClientProcess, Port.Value, _cts);
        }
        //
        // This means we couldn't access the process's handle, so we need to
        // retry getting the MTGO process again unless the user has provided
        // an invalid process handle manually.
        //
        catch (InvalidOperationException) when (!_processHandleOverride)
        {
          RefreshClientProcess(throwOnFailure: true);
          _processHandleOverride = true;
          throw;
        }
      },
      // Retry connecting to avoid creating a race condition
      delay: 500, retries: 10, raise: true); // 5s
    }

    // When the MTGO process exists, trigger the ProcessExited event
    ClientProcess.EnableRaisingEvents = true;
    ClientProcess.Exited += (s, e) =>
    {
      Log.Debug("MTGO process exited with code {ExitCode}.", ((Process)s).ExitCode);
      Dispose();
      ProcessExited?.Invoke(null, EventArgs.Empty);
    };

    // Verify that the injected assembly is loaded and reponding
    if (!handle.Communicator.CheckAliveness())
      throw new TimeoutException("Diver is not responding to requests.");
    else
      Log.Debug("Established a connection to the MTGO process.");

    return handle;
  }

  /// <summary>
  /// Checks the heartbeat of the MTGO process.
  /// </summary>
  /// <returns>True if the MTGO process is alive.</returns>
  public static bool CheckHeartbeat()
  {
    if (!Retry(@client.Communicator.CheckAliveness))
    {
      Log.Debug("Could not establish a connection to the MTGO process.");
      Dispose();
      ProcessExited?.Invoke(null, EventArgs.Empty);
      ProcessExited = null;
      return false;
    }

    return true;
  }

  /// <summary>
  /// Disconnects from the target process and disposes of the client handle.
  /// </summary>
  public static void Dispose()
  {
    if (_isDisposed) return;                     // Already disposed.
    if (!IsInitialized && !_isDisposing) return; // Nothing to dispose.

    lock (_disposeLock)
    {
      if (_isDisposed || _isDisposing) return;
      _isDisposing = true;
    }

    Log.Debug("Disposing RemoteClient.");

    //
    // Replace the current CancellationTokenSource with a fresh one so any late
    // callbacks that still hold a reference to the old CTS can safely cancel
    // it without hitting an ObjectDisposedException. We then cancel and dispose
    // the old CTS after the swap.
    //
    Try(() =>
    {
      var newCts = new CancellationTokenSource();
      var oldCts = Interlocked.Exchange(ref @this._cts, newCts);
      if (oldCts != null)
      {
        // Don't dispose old CTS to avoid races with DiverCommunicator.Cancel().
        try { if (!oldCts.IsCancellationRequested) oldCts.Cancel(); }
        catch (ObjectDisposedException) { }
      }
    });

    // Best-effort cleanup; swallow any individual errors.
    Try(@client.Dispose);

    if (CloseOnExit)
    {
      Try(ClientProcess.Kill);
      CloseOnExit = false;
    }
    ClientProcess = null!;
    Port = null;

    // Mark disposed before raising events so late subscribers can observe state.
    _isDisposed = true;
    _isDisposing = false;

    EventHandler? handlers;
    lock (_disposeLock)
    {
      handlers = _disposedHandlers;
      _disposedHandlers = null; // release references
    }
    Try(() => handlers?.Invoke(null, EventArgs.Empty));

    // Prepare a fresh lazy for future initialization attempts.
    s_instance = new Lazy<RemoteClient>(() => new RemoteClient());
    Log.Trace("RemoteClient disposed.");
  }

  /// <summary>
  /// Event raised when the target process has exited.
  /// </summary>
  public static event EventHandler? ProcessExited;

  /// <summary>
  /// Indicates whether the RemoteClient instance has fully disposed.
  /// </summary>
  public static bool IsDisposed => _isDisposed;

  /// <summary>
  /// Event raised when the RemoteClient is disposed. Future-only: handlers
  /// added after disposal will NOT be invoked. Use <see cref="IsDisposed"/>
  /// or <see cref="WaitForDisposeAsync"/> / <see cref="OnDisposed"/> for
  /// deterministic post-disposal logic.
  /// </summary>
  public static event EventHandler? Disposed
  {
    add
    {
      if (value is null) return;
      lock (_disposeLock)
      {
        if (_isDisposed) return; // future-only: ignore late subscribers
        _disposedHandlers += value;
      }
    }
    remove
    {
      if (value is null) return;
      lock (_disposeLock)
      {
        _disposedHandlers -= value;
      }
    }
  }
  private static EventHandler? _disposedHandlers;

  /// <summary>
  /// Waits for the RemoteClient to be disposed.
  /// </summary>
  public static async Task WaitForDisposeAsync()
  {
    if (_isDisposed) return; // Already disposed.
    if (!IsInitialized && !_isDisposing) return; // Never initialized.

    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    void Handler(object? s, EventArgs e) => tcs.TrySetResult(true);
    Disposed += Handler;
    try
    {
      if (_isDisposed) return; // Double-check after subscribing.
      await tcs.Task.ConfigureAwait(false);
    }
    finally
    {
      Disposed -= Handler;
    }
  }

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
    var queryRefs = GetInstances(queryPath).ToList();
    if (queryRefs.Count == 0)
      throw new InvalidOperationException($"Object '{queryPath}' not found.");

    if (queryRefs.Count > 1)
      throw new AmbiguousMatchException(
          $"Multiple objects named '{queryPath}' found.");

    return queryRefs.First();
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

    // If no objects were found, throw an exception
    if (!queryRefs.Any())
      throw new InvalidOperationException($"Object '{queryPath}' not found.");
  }

  /// <summary>
  /// Returns a single instance of a remote type from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote type.</param>
  /// <returns>A dynamic wrapper around the remote type.</returns>
  public static Type GetInstanceType(string queryPath)
  {
    var queryRefs = GetInstanceTypes(queryPath).ToList();
    if (queryRefs.Count == 0)
      throw new  InvalidOperationException($"Type '{queryPath}' not found.");

    if (queryRefs.Count > 1)
      throw new AmbiguousMatchException(
          $"Multiple types named '{queryPath}' found.");

    return queryRefs.First();
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

    // If no types were found, throw an exception
    if (!queryRefs.Any())
      throw new InvalidOperationException($"Type '{queryPath}' not found.");
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
    var methods = GetInstanceMethods(queryPath, methodName).ToList();
    if (methods.Count == 0)
      throw new MissingMethodException(
          $"Method '{methodName}' not found on remote type '{queryPath}'.");

    if (methods.Count > 1)
      throw new AmbiguousMatchException(
          $"Multiple methods named '{methodName}' found on remote type '{queryPath}'.");

    return methods[0];
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

    if (!methods.Any())
      throw new MissingMethodException(
          $"Method '{methodName}' not found on remote type '{queryPath}'.");

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
    if (!queryObject.IsValid)
      throw new InvalidOperationException($"Object '{queryPath}' could not be created.");

    return queryObject.Dynamify();
  }

  /// <summary>
  /// Creates a new instance of a remote object of type T.
  /// </summary>
  /// <typeparam name="T">The type of the remote object to create.</typeparam>
  /// <param name="parameters">The parameters to pass to the remote object's constructor.</param>
  /// <returns>A dynamic wrapper around the remote object.</returns>
  public static dynamic CreateInstance<T>(
    params object[] parameters)
  {
    return CreateInstance(typeof(T).FullName!, parameters);
  }

  /// <summary>
  /// Creates a new instance of a remote enum object from the given query path.
  /// </summary>
  /// <param name="queryPath">The query path to the remote enum object.</param>
  /// <param name="valueName">The name of the enum value to create.</param>
  /// <returns>A dynamic wrapper around the remote enum value.</returns>
  public static dynamic CreateEnum(
    string queryPath,
    string valueName)
  {
    var enumType = GetInstanceType(queryPath);
    var enumValue = enumType
      .GetField(valueName)
      .GetValue(null);

    return enumValue;
  }

  public static dynamic CreateEnum<T>(string valueName) =>
    CreateEnum(typeof(T).FullName!, valueName);

  public static void SetProperty(
    DynamicRemoteObject dro,
    string propertyName,
    object value)
  {
    var propInfo = dro.GetType().GetProperty(propertyName);
    if (propInfo is null)
      throw new MissingMemberException(
          $"Property '{propertyName}' not found on remote object.");

    propInfo.SetValue(dro, value);
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

  //
  // RemoteHarmony wrapper methods
  //

  /// <summary>
  /// Hooks a remote object's method using a Harmony callback.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="methodName">The name of the method to hook.</param>
  /// <param name="callback">The local Harmony callback to use.</param>
  public static void HookMethod(
    string queryPath,
    string methodName,
    HookAction callback)
  {
    MethodInfo method = GetInstanceMethod(queryPath, methodName);
    @client.Harmony.Patch(method, prefix: callback);
  }

  /// <summary>
  /// Unhooks a remote object's method using a Harmony callback.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="methodName">The name of the method to unhook.</param>
  /// <param name="callback">The local Harmony callback to use.</param>
  /// <returns>True if the method was successfully unhooked.</returns>
  public static bool UnhookMethod(
    string queryPath,
    string methodName,
    HookAction callback)
  {
    MethodInfo method = GetInstanceMethod(queryPath, methodName);
    return @client.Harmony.UnhookMethod(method, callback);
  }

  /// <summary>
  /// Checks if a remote object's method has a Harmony callback.
  /// </summary>
  /// <param name="queryPath">The query path to the remote object.</param>
  /// <param name="methodName">The name of the method to check.</param>
  /// <param name="callback">The local Harmony callback to check.</param>
  /// <returns>True if the method has the Harmony callback.</returns>
  public static bool MethodHasHook(
    string queryPath,
    string methodName,
    HookAction callback)
  {
    MethodInfo method = GetInstanceMethod(queryPath, methodName);
    return @client.Harmony.HasHook(method, callback);
  }
}
