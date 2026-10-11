/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using MTGOSDK.Core.Logging;


namespace ScubaDiver;

/// <summary>
/// Provides mechanisms for executing operations on the WPF UI thread or a
/// dedicated STA thread for WPF/COM operations that require specific threading.
/// </summary>
public static class STAThread
{
  private static readonly BlockingCollection<Action> s_workQueue = new();
  private static readonly Thread s_staThread;
  private static readonly CancellationTokenSource s_cts = new();

  //
  // Diagnostics counters
  //

  internal static long s_totalOps;
  internal static long s_dispatcherOps;
  internal static int s_pendingOps;
  internal static long s_totalDispatchTicks;
  internal static long s_timeoutCount;

  static STAThread()
  {
    s_staThread = new Thread(STAThreadWorker)
    {
      Name = "STAWorkerThread",
      IsBackground = true,
    };
    s_staThread.SetApartmentState(ApartmentState.STA);
    s_staThread.Start();
  }

  private static void STAThreadWorker()
  {
    Log.Debug("[STAThread] STA worker thread started.");
    try
    {
      foreach (var item in s_workQueue.GetConsumingEnumerable(s_cts.Token))
      {
        item();
      }
    }
    catch (OperationCanceledException)
    {
      Log.Debug("[STAThread] STA worker thread cancelled.");
    }
    catch (Exception ex)
    {
      Log.Error("[STAThread] STA worker thread crashed.", ex);
    }
    Log.Debug("[STAThread] STA worker thread exiting.");
  }

  /// <summary>
  /// Gets the WPF application dispatcher if available.
  /// </summary>
  private static Dispatcher GetApplicationDispatcher()
  {
    try
    {
      var app = Application.Current;
      if (app != null)
      {
        return app.Dispatcher;
      }
    }
    catch
    {
      // Application.Current may not be available
    }
    return null;
  }

  /// <summary>
  /// Executes an action on the WPF UI thread (via Dispatcher) or falls back to
  /// the dedicated STA thread if no dispatcher is available.
  /// </summary>
  /// <param name="action">The action to execute.</param>
  /// <param name="timeout">Optional timeout for the operation.</param>
  /// <exception cref="TimeoutException">Thrown if the operation times out.</exception>
  /// <remarks>
  /// A timeout ends the wait without cancelling an action already queued or running.
  /// </remarks>
  /// <exception cref="Exception">Rethrows any exception from the action.</exception>
  public static void Execute(Action action, TimeSpan? timeout = null)
  {
    Interlocked.Increment(ref s_totalOps);

    var dispatcher = GetApplicationDispatcher();
    var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);

    if (dispatcher != null && dispatcher.CheckAccess())
    {
      action();
      return;
    }

    if (dispatcher != null)
    {
      Interlocked.Increment(ref s_dispatcherOps);
      Interlocked.Increment(ref s_pendingOps);
      Task completion;
      try
      {
        var operation = dispatcher.InvokeAsync(() =>
        {
          long started = Stopwatch.GetTimestamp();
          try
          {
            action();
          }
          finally
          {
            Interlocked.Add(ref s_totalDispatchTicks, Stopwatch.GetTimestamp() - started);
          }
        }, DispatcherPriority.Normal);
        completion = operation.Task;
      }
      catch
      {
        Interlocked.Decrement(ref s_pendingOps);
        throw;
      }

      // A timeout only ends the caller's wait. Count the queued operation until
      // it completes or is aborted, including completion after that timeout.
      _ = completion.ContinueWith(_ => Interlocked.Decrement(ref s_pendingOps),
        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
      WaitForCompletion(completion, waitTimeout, "Dispatcher");
      return;
    }

    Log.Debug("[STAThread] No dispatcher available, using STA worker thread.");
    if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
    {
      action();
      return;
    }

    var completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    s_workQueue.Add(() =>
    {
      try
      {
        action();
        completionSource.SetResult(null);
      }
      catch (Exception error)
      {
        completionSource.SetException(error);
      }
    });
    WaitForCompletion(completionSource.Task, waitTimeout, "STA thread");
  }

  private static void WaitForCompletion(Task completion, TimeSpan timeout, string threadName)
  {
    try
    {
      if (completion.Wait(timeout)) return;
    }
    catch (AggregateException)
    {
      // Preserve the action's exception and stack rather than Task.Wait's wrapper.
      completion.GetAwaiter().GetResult();
    }

    Interlocked.Increment(ref s_timeoutCount);
    throw new TimeoutException($"{threadName} operation timed out after {timeout.TotalSeconds} seconds.");
  }

  /// <summary>
  /// Executes a function on the WPF UI thread (via Dispatcher) or falls back to
  /// the dedicated STA thread, and returns the result.
  /// </summary>
  /// <typeparam name="T">The return type.</typeparam>
  /// <param name="func">The function to execute.</param>
  /// <param name="timeout">Optional timeout for the operation.</param>
  /// <returns>The result of the function.</returns>
  public static T Execute<T>(Func<T> func, TimeSpan? timeout = null)
  {
    T result = default;
    Execute(() => { result = func(); }, timeout);
    return result;
  }

  /// <summary>
  /// Checks if the current thread is running in STA apartment state.
  /// </summary>
  public static bool IsSTAThread =>
    Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

  /// <summary>
  /// Checks if the current thread is the WPF dispatcher thread.
  /// </summary>
  public static bool IsDispatcherThread
  {
    get
    {
      var dispatcher = GetApplicationDispatcher();
      return dispatcher != null && dispatcher.CheckAccess();
    }
  }

  /// <summary>
  /// Stops the STA worker thread.
  /// </summary>
  public static void Stop()
  {
    s_cts.Cancel();
    s_workQueue.CompleteAdding();
  }

}
