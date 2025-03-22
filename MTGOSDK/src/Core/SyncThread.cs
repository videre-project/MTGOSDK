/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core;

/// <summary>
/// Wraps a generic method to be used as an event handler for subscription.
/// </summary>
/// <typeparam name="T">The type of the event arguments.</typeparam>
/// <param name="handler">The method to be wrapped.</param>
public static class SyncThread
{
  private static readonly CancellationTokenSource s_cancellationTokenSource = new();
  private static readonly CancellationToken s_cancellationToken =
    s_cancellationTokenSource.Token;

  private static readonly int s_minJobThreads = Environment.ProcessorCount >= 2 ? 2 : 1;
  private static readonly int s_maxJobThreads = Environment.ProcessorCount;
  private static readonly ConcurrentTaskScheduler s_taskScheduler =
    new(s_minJobThreads, s_maxJobThreads, s_cancellationToken);

  private static readonly TaskFactory s_taskFactory = new(s_taskScheduler);
  private static readonly ConcurrentDictionary<string, Task> s_groupTasks = new();

  private static DateTime _lastCleanup = DateTime.UtcNow;
  private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

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

    // Occasionally clean up completed tasks
    if (s_groupTasks.Count > 10 || DateTime.UtcNow - _lastCleanup > CleanupInterval)
    {
      CleanUpCompletedTasks();
      _lastCleanup = DateTime.UtcNow;
    }
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

      while (ex.InnerException != null)
      {
        Log.Error(ex.InnerException, "Inner exception: {0}", ex.InnerException.Message + "\n" + ex.InnerException.StackTrace);
        ex = ex.InnerException;
      }
    }
  };

  public static Task EnqueueAsync(Func<Task> callback)
  {
    return s_taskFactory.StartNew(async () => await WrapCallbackAsync(callback)(), s_cancellationToken).Unwrap();
  }

  public static Task EnqueueAsync(Func<Task> callback, string groupId)
  {
    if (string.IsNullOrEmpty(groupId))
    {
      return EnqueueAsync(callback);
    }

    Task newTask;
    if (s_groupTasks.TryGetValue(groupId, out var existingTask) &&
        !existingTask.IsCompleted && !existingTask.IsFaulted && !existingTask.IsCanceled)
    {
      newTask = existingTask.ContinueWith(
        async _ => await WrapCallbackAsync(callback)(),
        s_cancellationToken,
        TaskContinuationOptions.None,
        s_taskScheduler).Unwrap();
    }
    else
    {
      newTask = s_taskFactory.StartNew(async () => await WrapCallbackAsync(callback)(), s_cancellationToken).Unwrap();
    }

    s_groupTasks[groupId] = newTask;

    if (s_groupTasks.Count > 10 || DateTime.UtcNow - _lastCleanup > CleanupInterval)
    {
      CleanUpCompletedTasks();
      _lastCleanup = DateTime.UtcNow;
    }

    return newTask;
  }

  private static void CleanUpCompletedTasks()
  {
    foreach (var key in s_groupTasks.Keys)
    {
      if (s_groupTasks.TryGetValue(key, out var task) &&
          (task.IsCompleted || task.IsCanceled || task.IsFaulted))
      {
        s_groupTasks.TryRemove(key, out _);
      }
    }
  }

  public static void Stop()
  {
    s_cancellationTokenSource.Cancel();
  }
}
