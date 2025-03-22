/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.Core;

/// <summary>
/// A task scheduler that ensures a limited degree of concurrency.
/// </summary>
public class ConcurrentTaskScheduler(
    int minDegreeOfParallelism,
    int maxDegreeOfParallelism,
    CancellationToken cancellationToken) : TaskScheduler
{
  private readonly ConcurrentBag<object> _jobs = new();
  private readonly ConcurrentQueue<Task> _tasks = new();
  private readonly ConcurrentDictionary<Task, bool> _dequeuedTasks = new();
  private readonly SemaphoreSlim _semaphore = new(maxDegreeOfParallelism);

  private readonly bool _reserveThreads = minDegreeOfParallelism > 0;

  protected override IEnumerable<Task> GetScheduledTasks()
  {
    return _tasks.ToArray();
  }

  protected override void QueueTask(Task task)
  {
    _tasks.Enqueue(task);
    NotifyThreadPoolOfPendingWork();
  }

  private async void ExecuteTaskQueue(object? state)
  {
    try
    {
      // If the cancellation token is already canceled, return immediately.
      if (cancellationToken.IsCancellationRequested) return;

      await _semaphore.WaitAsync(cancellationToken);
      _jobs.Add(state);

      while (!cancellationToken.IsCancellationRequested)
      {
        // Wait up to 2 seconds for a task to be queued.
        if (_reserveThreads && !await WaitUntil(
          () => !_tasks.IsEmpty || cancellationToken.IsCancellationRequested,
          delay: 10,
          retries: 200))
        {
          if (_jobs.Count > minDegreeOfParallelism) break;
        }
        else
        {
          if (_tasks.TryDequeue(out Task? item))
          {
            // Check if the task was logically dequeued
            if (!_dequeuedTasks.ContainsKey(item))
            {
              try
              {
                base.TryExecuteTask(item);
              }
              finally
              {
                // Remove the task from the dictionary after execution
                _dequeuedTasks.TryRemove(item, out _);
              }
            }
            // Task was already dequeued, so skip it
            else
            {
              continue;
            }
          }
          else
          {
            if (_reserveThreads) continue;
            break;
          }
        }
      }
    }
    finally
    {
      _jobs.TryTake(out _);
      _semaphore.Release();
    }
  }

  private void NotifyThreadPoolOfPendingWork()
  {
    if (_jobs.Count < maxDegreeOfParallelism || !_tasks.IsEmpty)
    {
      ThreadPool.UnsafeQueueUserWorkItem(ExecuteTaskQueue, null);
    }
  }

  protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued)
  {
    if (previouslyQueued)
    {
      if (!_dequeuedTasks.ContainsKey(task))
      {
        _dequeuedTasks.TryAdd(task, true);
        return base.TryExecuteTask(task);
      }
      else
      {
        return false;
      }
    }
    else
    {
      return base.TryExecuteTask(task);
    }
  }

  protected override bool TryDequeue(Task task)
  {
    // Logically dequeue the task by adding it to the dictionary
    return _dequeuedTasks.TryAdd(task, true);
  }
}
