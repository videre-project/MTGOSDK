/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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

  private static readonly int s_maxDegreeOfParallelism =
    Environment.ProcessorCount;
  private static readonly ConcurrentTaskScheduler s_taskScheduler =
    new(s_maxDegreeOfParallelism, s_cancellationToken);
  private static readonly TaskFactory s_taskFactory = new(s_taskScheduler);

  private static Action WrapCallback(Action callback) => () =>
  {
    try
    {
      callback();
    }
    catch (Exception ex)
    {
      Log.Error(ex, "An error occurred while executing a callback.");
    }
  };

  public static void Enqueue(Action callback)
  {
    s_taskFactory.StartNew(WrapCallback(callback), s_cancellationToken);
  }

  public static void Stop()
  {
    s_cancellationTokenSource.Cancel();
  }
}
