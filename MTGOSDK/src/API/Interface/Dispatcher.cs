/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.API.Interface;

/// <summary>
/// Coordinates readiness of WPF views in the client process.
/// </summary>
public static class Dispatcher
{
  /// <summary>
  /// Waits until a remote view is loaded and its dispatcher executes an idle
  /// operation that verifies that condition.
  /// </summary>
  /// <remarks>
  /// Observes Loaded before checking existing state, so attachment works both
  /// before and after loading. Waiting performs no polling and has no fixed UI
  /// dispatch timeout. Cancellation aborts a queued readiness operation.
  /// Completion establishes that the view was loaded when the idle operation ran.
  /// Subsequent UI access must still use the view's owning dispatcher.
  /// </remarks>
  /// <param name="view">A remote WPF FrameworkElement or its SDK wrapper.</param>
  /// <param name="cancellationToken">Cancels the pending readiness wait.</param>
  public static Task WaitForReadyAsync(object view,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(view);
    if (DLRWrapper.Unbind((dynamic)view) is not DynamicRemoteObject target)
      throw new ArgumentException("Expected a remote WPF view.", nameof(view));

    // Registering the remote events requires IPC. Run that setup asynchronously
    // so the caller can continue processing while the client loads the view.
    return Task.Run(async () =>
    {
      bool previous = DiverCommunicator.ForceUIThread;
      DiverCommunicator.ForceUIThread = false;
      try
      {
        await WaitForLoadedAsync(target, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        DiverCommunicator.ForceUIThread = previous;
      }
    }, cancellationToken);
  }

  private static async Task WaitForLoadedAsync(DynamicRemoteObject view,
    CancellationToken cancellationToken)
  {
    var readinessLock = new object();
    var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var changes = new EventProxy(view, "Loaded");
    using var loadedSubscription = changes;
    changes += (Action<dynamic, dynamic>)((sender, args) =>
    {
      lock (readinessLock) loaded.TrySetResult(true);
    });

    var delegateType = RemoteClient.GetInstanceType("System.Func`1[System.Boolean]");
    var factory = RemoteClient.GetInstanceType("System.Delegate").GetMethod(
      "CreateDelegate", [typeof(Type), typeof(object), typeof(string)])!;
    object readLoaded = factory.Invoke(null, [delegateType, view, "get_IsLoaded"])!;
    object dispatcher = ((dynamic)view).Dispatcher;
    object priority = RemoteClient.CreateEnum(
      "System.Windows.Threading.DispatcherPriority", "ContextIdle");
    var shutdown = new EventProxy(dispatcher, "ShutdownStarted");
    using var shutdownSubscription = shutdown;
    shutdown += (Action<dynamic, dynamic>)((sender, args) =>
    {
      lock (readinessLock) loaded.TrySetException(new InvalidOperationException(
        "The client dispatcher is shutting down."));
    });

    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Task notification;
      lock (readinessLock)
      {
        if (loaded.Task.Status == TaskStatus.RanToCompletion)
          loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        notification = loaded.Task;
      }

      if (await ReadLoadedAtIdleAsync(dispatcher, priority, readLoaded,
        cancellationToken).ConfigureAwait(false))
        return;

      // Recheck only after a notification, including one received during the
      // queued check. Native IsLoaded is always read on the owning UI thread.
      await notification.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  private static async Task<bool> ReadLoadedAtIdleAsync(object dispatcher,
    object priority, object readLoaded, CancellationToken cancellationToken)
  {
    if (((dynamic)dispatcher).HasShutdownStarted)
      throw new InvalidOperationException("The client dispatcher is shutting down.");
    dynamic operation = ((dynamic)dispatcher).BeginInvoke(priority, readLoaded);
    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var completed = new EventProxy(operation, "Completed");
    var aborted = new EventProxy(operation, "Aborted");
    using var completedSubscription = completed;
    using var abortedSubscription = aborted;

    void Complete()
    {
      if (completion.Task.IsCompleted) return;
      try
      {
        completion.TrySetResult((bool)operation.Result);
      }
      catch (Exception error)
      {
        completion.TrySetException(error);
      }
    }

    try
    {
      completed += (Action<dynamic, dynamic>)((sender, args) => Complete());
      aborted += (Action<dynamic, dynamic>)((sender, args) =>
        completion.TrySetException(new InvalidOperationException(
          "The client dispatcher aborted the readiness operation.")));
      using var cancellation = cancellationToken.Register(() =>
        completion.TrySetCanceled(cancellationToken));

      // Completion may precede subscription. WPF retains the terminal status.
      string status = operation.Status.ToString();
      if (status == "Completed")
        Complete();
      else if (status == "Aborted")
        throw new InvalidOperationException("The client dispatcher aborted the readiness operation.");

      return await completion.Task.ConfigureAwait(false);
    }
    finally
    {
      // Abort is thread-safe and returns immediately if execution has begun.
      DLRWrapper.Try(() => operation.Abort());
    }
  }
}
