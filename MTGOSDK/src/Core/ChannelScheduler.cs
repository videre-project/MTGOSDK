/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Channels;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core;

/// <summary>
/// A high-throughput, unbounded work scheduler using Channel for queueing
/// and ThreadPool for execution. Optimized for burst workloads.
/// </summary>
public sealed class ChannelScheduler : IDisposable
{
  private readonly Channel<Func<Task>> _channel;
  private readonly CancellationTokenSource _cts = new();
  private readonly int _workerCount;
  private readonly Task[] _workers;

  /// <summary>
  /// Creates a new ChannelScheduler with the specified number of workers.
  /// </summary>
  /// <param name="workerCount">
  /// Number of concurrent workers. Defaults to ProcessorCount.
  /// </param>
  public ChannelScheduler(int workerCount = 0)
  {
    _workerCount = workerCount > 0 ? workerCount : Environment.ProcessorCount;

    _channel = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
    {
      SingleWriter = false,  // Allow concurrent enqueues
      SingleReader = false   // Multiple workers consume
    });

    // Start workers on ThreadPool
    _workers = new Task[_workerCount];
    for (int i = 0; i < _workerCount; i++)
    {
      _workers[i] = Task.Run(() => WorkerLoopAsync(_cts.Token));
    }
  }

  private async Task WorkerLoopAsync(CancellationToken ct)
  {
    try
    {
      await foreach (var work in _channel.Reader.ReadAllAsync(ct))
      {
        try
        {
          await work();
        }
        catch (Exception ex)
        {
          Log.Error(ex, "ChannelScheduler work item failed.");
        }
      }
    }
    catch (OperationCanceledException)
    {
      // Expected on shutdown
    }
  }

  /// <summary>
  /// Enqueues a synchronous action for execution.
  /// </summary>
  public void Enqueue(Action action)
  {
    _channel.Writer.TryWrite(() =>
    {
      action();
      return Task.CompletedTask;
    });
  }

  /// <summary>
  /// Enqueues an async action for execution.
  /// </summary>
  public void Enqueue(Func<Task> asyncAction)
  {
    _channel.Writer.TryWrite(asyncAction);
  }

  /// <summary>
  /// Number of items currently queued.
  /// </summary>
  public int QueuedCount => _channel.Reader.Count;

  /// <summary>
  /// Gracefully shuts down the scheduler, completing remaining work.
  /// </summary>
  public void Dispose()
  {
    _channel.Writer.Complete();

    try
    {
      // Wait for workers to drain remaining items
      Task.WaitAll(_workers, TimeSpan.FromSeconds(5));
    }
    catch (AggregateException)
    {
      // Workers may have been cancelled
    }

    _cts.Cancel();
    _cts.Dispose();
  }
}
