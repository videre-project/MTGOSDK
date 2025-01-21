/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace MTGOSDK.Core;

/// <summary>
/// A task scheduler that ensures a limited degree of concurrency.
/// </summary>
public class ConcurrentTaskScheduler(
  int maxDegreeOfParallelism,
  CancellationToken cancellationToken) : TaskScheduler
{
  private readonly LinkedList<Task> _tasks = new();
  private readonly SemaphoreSlim _semaphore = new(maxDegreeOfParallelism);

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

  private void NotifyThreadPoolOfPendingWork()
  {
    // Skip propagating the CAS restrictions of the current thread to quickly
    // dispatch the work item to another thread in the thread pool.
    ThreadPool.UnsafeQueueUserWorkItem(async _ =>
    {
      try
      {
        await _semaphore.WaitAsync(cancellationToken);

        while (true)
        {
          Task item;
          lock (_tasks)
          {
            if (_tasks.Count == 0)
              break;

            item = _tasks.First.Value;
            _tasks.RemoveFirst();
          }
          base.TryExecuteTask(item);
        }
      }
      finally
      {
        _semaphore.Release();
      }
    }, null);
  }

  protected override bool TryExecuteTaskInline(
    Task task,
    bool taskWasPreviouslyQueued)
  {
    if (taskWasPreviouslyQueued)
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
