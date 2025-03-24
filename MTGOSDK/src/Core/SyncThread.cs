/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core;

/// <summary>
/// Wraps a generic method to be used as an event handler for subscription.
/// </summary>
/// <typeparam name="T">The type of the event arguments.</typeparam>
/// <param name="handler">The method to be wrapped.</param>
public static class SyncThread
{
  private static readonly int s_maxQueueSize = Environment.ProcessorCount * 100;
  private static readonly SemaphoreSlim s_queueSemaphore = new(s_maxQueueSize, s_maxQueueSize);

  private static readonly CancellationTokenSource s_cancellationTokenSource = new();
  private static readonly CancellationToken s_cancellationToken =
    s_cancellationTokenSource.Token;

  private static readonly int s_minJobThreads = Environment.ProcessorCount >= 2 ? 2 : 1;
  private static readonly int s_maxJobThreads = Environment.ProcessorCount;
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

    // Wait for queue space with optional timeout
    if (timeout.HasValue)
    {
      if (!await s_queueSemaphore.WaitAsync(timeout.Value, s_cancellationToken))
        throw new TimeoutException("Task queue is full");
    }
    else
    {
      await s_queueSemaphore.WaitAsync(s_cancellationToken);
    }

    try
    {
      await s_taskFactory.StartNew(WrapCallbackAsync(async () =>
      {
        try
        {
          await callback();
        }
        finally
        {
          s_queueSemaphore.Release();
        }
      }), s_cancellationToken);
    }
    catch
    {
      s_queueSemaphore.Release();
      throw;
    }
  }

  public static async Task EnqueueAsync(Func<Task> callback, string groupId, TimeSpan? timeout = null)
  {
    if (string.IsNullOrEmpty(groupId))
    {
      await EnqueueAsync(callback);
      return;
    }

    if (s_cancellationToken.IsCancellationRequested)
      return;

    // Wait for queue space
    if (timeout.HasValue)
    {
      if (!await s_queueSemaphore.WaitAsync(timeout.Value, s_cancellationToken))
          throw new TimeoutException("Task queue is full");
    }
    else
    {
      await s_queueSemaphore.WaitAsync(s_cancellationToken);
    }

    try
    {
      Task newTask;
      if (s_groupTasks.TryGetValue(groupId, out var existingTask) &&
          !existingTask.IsCompleted && !existingTask.IsFaulted && !existingTask.IsCanceled)
      {
        newTask = existingTask.ContinueWith(
          _ => WrapCallbackAsync(async () =>
          {
            try
            {
              await callback();
            }
            finally
            {
              s_queueSemaphore.Release();
            }
          })(),
          s_cancellationToken,
          TaskContinuationOptions.None,
          s_taskScheduler);
      }
      else
      {
        newTask = s_taskFactory.StartNew(WrapCallbackAsync(async () =>
        {
          try
          {
            await callback();
          }
          finally
          {
            s_queueSemaphore.Release();
          }
        }), s_cancellationToken);
      }

      s_groupTasks[groupId] = newTask;
    }
    catch
    {
      s_queueSemaphore.Release();
      throw;
    }
  }

  public static void Stop()
  {
    s_cancellationTokenSource.Cancel();
    s_cleanupTimer?.Dispose();
  }
}
