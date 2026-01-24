/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core;

/// <summary>
/// A high-performance thread pool wrapper for executing and grouping tasks.
/// </summary>
public static class SyncThread
{
  private static readonly CancellationTokenSource s_cancellationTokenSource = new();
  private static readonly CancellationToken s_cancellationToken =
    s_cancellationTokenSource.Token;

  // Increased thread limits for high-volume IPC operations
  // (DispatcherObject detection limits UI thread calls to ~0.5% of operations)
  private static readonly int s_minJobThreads = Math.Max(4, Environment.ProcessorCount);
  private static readonly int s_maxJobThreads = Environment.ProcessorCount * 2;
  private static readonly ConcurrentTaskScheduler s_taskScheduler =
    new(s_minJobThreads, s_maxJobThreads, s_cancellationToken);

  private static readonly TaskFactory s_taskFactory = new(s_taskScheduler);
  private static readonly ConcurrentDictionary<string, Task> s_groupTasks = new();

  private static readonly Timer s_cleanupTimer;
  private static readonly TimeSpan s_cleanupInterval = TimeSpan.FromSeconds(30);

  static SyncThread()
  {
    // Run cleanup every 30 seconds
    s_cleanupTimer = new(CleanupCallback, null, TimeSpan.Zero, s_cleanupInterval);
  }

  private static void CleanupCallback(object? state)
  {
    try
    {
      foreach (var key in s_groupTasks.Keys.ToList())
      {
        if (s_groupTasks.TryGetValue(key, out var task) &&
            (task.IsCompleted || task.IsCanceled || task.IsFaulted))
        {
          // Only remove if the task is still in the same state
          s_groupTasks.TryRemove(key, out _);
        }
      }
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to clean up completed tasks");
    }
  }

  private static Action WrapCallback(Action callback) => () =>
  {
    if (s_cancellationToken.IsCancellationRequested) return;

    try
    {
      callback();
    }
    catch (Exception ex)
    {
      Log.Error(ex, "An error occurred while executing a callback.");
      Log.Debug(ex.StackTrace);

      // Loop through all inner exceptions
      while (ex.InnerException != null)
      {
        Log.Error(ex.InnerException, "Inner exception: {0}", ex.InnerException.Message + "\n" + ex.InnerException.StackTrace);
        ex = ex.InnerException;
      }
    }
  };

  public static void Enqueue(Action callback)
  {
    s_taskFactory.StartNew(WrapCallback(callback), s_cancellationToken);
  }

  public static void Enqueue(Action callback, string groupId)
  {
    if (string.IsNullOrEmpty(groupId))
    {
      // Fall back to the non-grouped version
      Enqueue(callback);
      return;
    }

    Task newTask;
    if (s_groupTasks.TryGetValue(groupId, out var existingTask) &&
        !existingTask.IsCompleted && !existingTask.IsFaulted && !existingTask.IsCanceled)
    {
      // Continue from previous task in this group to ensure order
      newTask = existingTask.ContinueWith(
        _ => WrapCallback(callback)(),
        s_cancellationToken,
        // Defers continuation to the current synchronization context and does
        // not allow for the continuation to be scheduled on a different thread.
        TaskContinuationOptions.None,
        s_taskScheduler);
    }
    else
    {
      // Create a new task as no valid task exists for this group
      newTask = s_taskFactory.StartNew(WrapCallback(callback), s_cancellationToken);
    }

    // Update the group dictionary
    s_groupTasks[groupId] = newTask;

    // // Occasionally clean up completed tasks
    // if (s_groupTasks.Count > 10 || DateTime.UtcNow - _lastCleanup > CleanupInterval)
    // {
    //   CleanUpCompletedTasks();
    //   _lastCleanup = DateTime.UtcNow;
    // }
  }

  private static Func<Task> WrapCallbackAsync(Func<Task> callback) => async () =>
  {
    if (s_cancellationToken.IsCancellationRequested) return;

    try
    {
      await callback();
    }
    catch (Exception ex)
    {
      Log.Error(ex, "An error occurred while executing a callback.");
      Log.Debug(ex.StackTrace);

      while (ex.InnerException != null)
      {
        Log.Error(ex.InnerException, "Inner exception: {0}", ex.InnerException.Message + "\n" + ex.InnerException.StackTrace);
        ex = ex.InnerException;
      }
    }
  };

  public static async Task EnqueueAsync(
    Func<Task> callback,
    TimeSpan? timeout = null)
  {
    if (s_cancellationToken.IsCancellationRequested)
      return;

    // IMPORTANT: StartNew with async Func<Task> returns Task<Task>.
    // Must Unwrap() to await the inner async work, not just the scheduling.
    await s_taskFactory.StartNew(WrapCallbackAsync(callback), s_cancellationToken).Unwrap();
  }

  public static async Task EnqueueAsync(
    Func<Task> callback,
    string groupId,
    TimeSpan? timeout = null)
  {
    if (string.IsNullOrEmpty(groupId))
    {
      await EnqueueAsync(callback);
      return;
    }

    if (s_cancellationToken.IsCancellationRequested)
      return;

    Task newTask;
    if (s_groupTasks.TryGetValue(groupId, out var existingTask) &&
        !existingTask.IsCompleted && !existingTask.IsFaulted && !existingTask.IsCanceled)
    {
      newTask = existingTask.ContinueWith(
        _ => WrapCallbackAsync(callback)(),
        s_cancellationToken,
        TaskContinuationOptions.None,
        s_taskScheduler);
    }
    else
    {
      newTask = s_taskFactory.StartNew(WrapCallbackAsync(callback), s_cancellationToken);
    }

    s_groupTasks[groupId] = newTask;
  }

  public static void Stop()
  {
    s_cancellationTokenSource.Cancel();
    s_cleanupTimer?.Dispose();
  }
}
