/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Threading;
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
  private static readonly BlockingCollection<STAWorkItem> s_workQueue = new();
  private static readonly Thread s_staThread;
  private static readonly CancellationTokenSource s_cts = new();

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
        item.Execute();
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
  /// <exception cref="Exception">Rethrows any exception from the action.</exception>
  public static void Execute(Action action, TimeSpan? timeout = null)
  {
    var dispatcher = GetApplicationDispatcher();
    var waitTimeout = timeout ?? TimeSpan.FromSeconds(60);

    // If we're already on the dispatcher thread, execute directly
    if (dispatcher != null && dispatcher.CheckAccess())
    {
      action();
      return;
    }

    // Try to use the WPF Dispatcher first (for UI thread access)
    if (dispatcher != null)
    {
      Log.Debug("[STAThread] Executing on WPF Dispatcher thread.");
      Exception caughtException = null;

      var operation = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
      {
        try
        {
          action();
        }
        catch (Exception ex)
        {
          caughtException = ex;
        }
      }));

      // Wait for completion with timeout
      var result = operation.Wait(waitTimeout);
      if (result != DispatcherOperationStatus.Completed)
      {
        throw new TimeoutException($"Dispatcher operation timed out after {waitTimeout.TotalSeconds} seconds.");
      }

      if (caughtException != null)
      {
        throw caughtException;
      }
      return;
    }

    // Fall back to dedicated STA thread if no dispatcher available
    Log.Debug("[STAThread] No dispatcher available, using STA worker thread.");
    if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
    {
      // Already on an STA thread, execute directly
      action();
      return;
    }

    var workItem = new STAWorkItem(action);
    s_workQueue.Add(workItem);

    if (!workItem.WaitForCompletion(waitTimeout))
    {
      throw new TimeoutException($"STA thread operation timed out after {waitTimeout.TotalSeconds} seconds.");
    }

    workItem.ThrowIfFaulted();
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
  /// Determines if an exception indicates that UI thread/Dispatcher execution is required.
  /// </summary>
  /// <param name="ex">The exception to check.</param>
  /// <returns>True if the exception suggests UI thread is needed.</returns>
  public static bool RequiresSTAThread(Exception ex)
  {
    if (ex == null) return false;

    // Check the exception and all inner exceptions
    var current = ex;
    while (current != null)
    {
      // Common STA/UI thread-related exception messages
      if (current is InvalidOperationException)
      {
        var msg = current.Message?.ToLowerInvariant() ?? "";
        if (msg.Contains("sta") ||
            msg.Contains("single-threaded apartment") ||
            msg.Contains("calling thread must be sta") ||
            msg.Contains("different thread owns") ||
            msg.Contains("dispatcher") ||
            msg.Contains("wpf") ||
            msg.Contains("ui thread") ||
            msg.Contains("freezable") ||
            msg.Contains("across threads") ||
            msg.Contains("cannot be frozen"))
        {
          return true;
        }
      }

      // XamlParseException can wrap threading issues
      if (current.GetType().Name == "XamlParseException")
      {
        var msg = current.Message?.ToLowerInvariant() ?? "";
        if (msg.Contains("freezable") ||
            msg.Contains("across threads") ||
            msg.Contains("cannot be frozen"))
        {
          return true;
        }
      }

      // COM threading exceptions
      if (current.HResult == unchecked((int)0x8001010E) || // RPC_E_WRONG_THREAD
          current.HResult == unchecked((int)0x80010100) || // RPC_E_SYS_CALL_FAILED
          current.HResult == unchecked((int)0x800401F0))   // CO_E_NOTINITIALIZED
      {
        return true;
      }

      current = current.InnerException;
    }

    return false;
  }

  /// <summary>
  /// Stops the STA worker thread.
  /// </summary>
  public static void Stop()
  {
    s_cts.Cancel();
    s_workQueue.CompleteAdding();
  }

  private class STAWorkItem
  {
    private readonly Action _action;
    private readonly ManualResetEventSlim _signal;
    private Exception _exception;

    public STAWorkItem(Action action)
    {
      _action = action;
      _signal = new ManualResetEventSlim(false);
    }

    public void Execute()
    {
      try
      {
        _action();
      }
      catch (Exception ex)
      {
        _exception = ex;
      }
      finally
      {
        _signal.Set();
      }
    }

    public bool WaitForCompletion(TimeSpan timeout)
    {
      return _signal.Wait(timeout);
    }

    public void ThrowIfFaulted()
    {
      if (_exception != null)
      {
        throw _exception;
      }
    }
  }
}
