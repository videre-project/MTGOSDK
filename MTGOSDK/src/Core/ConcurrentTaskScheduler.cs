/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;


namespace MTGOSDK.Core;

/// <summary>
/// A task scheduler that ensures a limited degree of concurrency with immediate
/// task pickup using signaling instead of polling.
/// </summary>
public class ConcurrentTaskScheduler : TaskScheduler
{
  private readonly int _minDegreeOfParallelism;
  private readonly int _maxDegreeOfParallelism;
  private readonly CancellationToken _cancellationToken;
  
  private readonly ConcurrentQueue<Task> _tasks = new();
  private readonly ConcurrentDictionary<Task, bool> _dequeuedTasks = new();
  private readonly SemaphoreSlim _concurrencySemaphore;
  
  // Signaling mechanism for immediate task pickup (replaces polling)
  private readonly AutoResetEvent _taskAvailable = new(false);
  
  // Track active workers
  private int _activeWorkers = 0;
  
  public ConcurrentTaskScheduler(
    int minDegreeOfParallelism,
    int maxDegreeOfParallelism,
    CancellationToken cancellationToken)
  {
    _minDegreeOfParallelism = minDegreeOfParallelism;
    _maxDegreeOfParallelism = maxDegreeOfParallelism;
    _cancellationToken = cancellationToken;
    _concurrencySemaphore = new SemaphoreSlim(maxDegreeOfParallelism);
    
    // Start reserved worker threads immediately
    for (int i = 0; i < minDegreeOfParallelism; i++)
    {
      SpawnWorker(isReserved: true);
    }
  }

  protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();

  protected override void QueueTask(Task task)
  {
    _tasks.Enqueue(task);
    
    // Signal waiting workers that a task is available
    _taskAvailable.Set();
    
    // Spawn additional worker if below max and we have pending work
    SpawnWorkerIfNeeded();
  }

  private void SpawnWorkerIfNeeded()
  {
    // Only spawn if we're under max parallelism and have tasks
    int currentWorkers = Volatile.Read(ref _activeWorkers);
    if (currentWorkers < _maxDegreeOfParallelism && !_tasks.IsEmpty)
    {
      SpawnWorker(isReserved: false);
    }
  }

  private void SpawnWorker(bool isReserved)
  {
    ThreadPool.UnsafeQueueUserWorkItem(_ => ExecuteTaskLoop(isReserved), null);
  }

  private void ExecuteTaskLoop(bool isReserved)
  {
    // Try to acquire a concurrency slot
    if (!_concurrencySemaphore.Wait(0))
    {
      // No slot available, exit
      return;
    }
    
    Interlocked.Increment(ref _activeWorkers);
    
    try
    {
      while (!_cancellationToken.IsCancellationRequested)
      {
        if (_tasks.TryDequeue(out Task? task))
        {
          // Check if task was logically dequeued (e.g., via TryDequeue)
          if (!_dequeuedTasks.ContainsKey(task))
          {
            try
            {
              base.TryExecuteTask(task);
            }
            finally
            {
              _dequeuedTasks.TryRemove(task, out _);
            }
          }
          // Immediately try next task without waiting
          continue;
        }
        
        // No tasks available
        if (isReserved)
        {
          // Reserved workers wait indefinitely for new tasks (with timeout for cancellation check)
          _taskAvailable.WaitOne(100); // Short timeout to check cancellation
        }
        else
        {
          // Non-reserved workers exit when no work is available
          break;
        }
      }
    }
    finally
    {
      Interlocked.Decrement(ref _activeWorkers);
      _concurrencySemaphore.Release();
    }
  }

  protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued)
  {
    // Only inline if we can acquire a concurrency slot
    if (!_concurrencySemaphore.Wait(0))
      return false;
    
    try
    {
      if (previouslyQueued)
      {
        // Mark as logically dequeued if it was queued
        if (!_dequeuedTasks.TryAdd(task, true))
          return false;
      }
      return base.TryExecuteTask(task);
    }
    finally
    {
      _concurrencySemaphore.Release();
      if (previouslyQueued)
        _dequeuedTasks.TryRemove(task, out _);
    }
  }

  protected override bool TryDequeue(Task task)
  {
    // Logically dequeue by marking it
    return _dequeuedTasks.TryAdd(task, true);
  }
}

