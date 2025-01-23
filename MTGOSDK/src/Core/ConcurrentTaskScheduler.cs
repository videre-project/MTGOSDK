/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
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
  private readonly List<object> _jobs = new();
  private readonly LinkedList<Task> _tasks = new();
  private readonly SemaphoreSlim _semaphore = new(maxDegreeOfParallelism);

  private readonly bool _reserveThreads = minDegreeOfParallelism > 0;

  protected override IEnumerable<Task> GetScheduledTasks()
  {
    lock (_tasks)
    {
      return _tasks.ToArray();
    }
  }

  protected override void QueueTask(Task task)
  {
    lock (_tasks)
    {
      _tasks.AddLast(task);
      NotifyThreadPoolOfPendingWork();
    }
  }

  private async void ExecuteTaskQueue(object? state)
  {
    try
    {
      await _semaphore.WaitAsync(cancellationToken);
      lock (_jobs)
      {
        _jobs.Add(state);
      }

      while (!cancellationToken.IsCancellationRequested)
      {
        // Wait up to 2 seconds for a task to be queued.
        if (_reserveThreads && !await WaitUntil(() => _tasks.Any(), delay: 100))
        {
          lock (_jobs)
          {
            if (_jobs.Count > minDegreeOfParallelism) break;
          }
        }
        else
        {
          Task item;
          lock (_tasks)
          {
            if (_tasks.Count == 0)
            {
              if (_reserveThreads) continue;
              break;
            }

            item = _tasks.First.Value;
            _tasks.RemoveFirst();
          }
          base.TryExecuteTask(item);
        }
      }
    }
    finally
    {
      lock (_jobs)
      {
        _jobs.Remove(state);
      }
      _semaphore.Release();
    }
  }

  private void NotifyThreadPoolOfPendingWork()
  {
    lock (_jobs)
    {
      // If we have reached the maximum degree of parallelism allowed, do not
      // queue any more jobs and instead wait for the current jobs to complete.
      if (_jobs.Count == maxDegreeOfParallelism) return;

      lock (_tasks)
      {
        // If there are no more tasks to execute, do not queue any more jobs.
        if (_jobs.Count >= _tasks.Count) return;
      }
    }

    // Skip propagating the CAS restrictions of the current thread to quickly
    // dispatch the work item to another thread in the thread pool.
    ThreadPool.UnsafeQueueUserWorkItem(ExecuteTaskQueue, null);
  }

  protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued)
  {
    if (previouslyQueued)
    {
      if (TryDequeue(task))
      {
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
    lock (_tasks)
    {
      return _tasks.Remove(task);
    }
  }
}
