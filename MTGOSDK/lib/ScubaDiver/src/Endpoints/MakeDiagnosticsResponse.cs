/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

using MTGOSDK.Core;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  // Cached CLR memory perf counters for per-generation heap sizes.
  // Lazy-init so we only pay the cost if diagnostics are actually queried.
  private static readonly Lazy<PerformanceCounter> s_gen0Heap = new(() =>
    new PerformanceCounter(".NET CLR Memory", "Gen 0 heap size",
      Process.GetCurrentProcess().ProcessName));
  private static readonly Lazy<PerformanceCounter> s_gen1Heap = new(() =>
    new PerformanceCounter(".NET CLR Memory", "Gen 1 heap size",
      Process.GetCurrentProcess().ProcessName));
  private static readonly Lazy<PerformanceCounter> s_gen2Heap = new(() =>
    new PerformanceCounter(".NET CLR Memory", "Gen 2 heap size",
      Process.GetCurrentProcess().ProcessName));
  private static readonly Lazy<PerformanceCounter> s_lohHeap = new(() =>
    new PerformanceCounter(".NET CLR Memory", "Large Object Heap size",
      Process.GetCurrentProcess().ProcessName));

  private static long SafeReadCounter(Lazy<PerformanceCounter> counter)
  {
    try { return counter.Value.RawValue; } catch { return -1; }
  }

  private byte[] MakeDiagnosticsResponse()
  {
    var (active, queued, max) = SyncThread.GetPoolMetrics();

    var endpoints = new Dictionary<string, EndpointStats>();
    foreach (var kvp in s_endpointMetrics)
    {
      long count = Interlocked.Read(ref kvp.Value.Count);
      if (count == 0) continue;

      double tickFreq = Stopwatch.Frequency;
      endpoints[kvp.Key] = new EndpointStats
      {
        Count = count,
        AvgMs = (Interlocked.Read(ref kvp.Value.TotalTicks) / (double)count)
                / tickFreq * 1000.0,
        LastMs = Interlocked.Read(ref kvp.Value.LastTicks)
                 / tickFreq * 1000.0,
      };
    }

    long dispatcherOps = Interlocked.Read(ref STAThread.s_dispatcherOps);
    double staAvgMs = dispatcherOps > 0
      ? (Interlocked.Read(ref STAThread.s_totalDispatchTicks) / (double)dispatcherOps)
        / Stopwatch.Frequency * 1000.0
      : 0;

    // Per-generation heap sizes via CLR performance counters
    long gen0 = SafeReadCounter(s_gen0Heap);
    long gen1 = SafeReadCounter(s_gen1Heap);
    long gen2 = SafeReadCounter(s_gen2Heap);
    long loh  = SafeReadCounter(s_lohHeap);

    // Probe WPF Dispatcher responsiveness: post a no-op at Background
    // priority and measure how long the Dispatcher takes to execute it.
    double dispatcherMs = -1;
    try
    {
      var dispatcher = Application.Current?.Dispatcher;
      if (dispatcher != null)
      {
        var sw = Stopwatch.StartNew();
        var signal = new ManualResetEventSlim(false);
        dispatcher.BeginInvoke(DispatcherPriority.Background,
          new Action(() => signal.Set()));
        if (signal.Wait(2000)) // 2s timeout
          dispatcherMs = sw.Elapsed.TotalMilliseconds;
        else
          dispatcherMs = 2000; // timed out — UI thread is blocked
        signal.Dispose();
      }
    }
    catch { /* Dispatcher unavailable */ }

    return WrapSuccess(new DiverDiagnostics
    {
      SyncThreadActive = active,
      SyncThreadQueued = queued,
      SyncThreadMax = max,
      ActiveHooks = _remoteHooks.Count,
      ActiveEventSubscriptions = _remoteEventHandler.Count,
      ConnectedClients = _tcpServer?.ClientCount ?? 0,
      Endpoints = endpoints,
      CallbacksSent = Interlocked.Read(ref s_callbacksSent),
      LastCallbackQueueDelayMs =
        new TimeSpan(Interlocked.Read(ref s_lastCallbackQueueDelayTicks))
          .TotalMilliseconds,
      StaThread = new StaThreadStats
      {
        TotalOps = Interlocked.Read(ref STAThread.s_totalOps),
        DispatcherOps = dispatcherOps,
        PendingOps = Volatile.Read(ref STAThread.s_pendingOps),
        AvgDispatchMs = staAvgMs,
        Timeouts = Interlocked.Read(ref STAThread.s_timeoutCount),
      },
      HostProcess = new HostProcessStats
      {
        GcGen0Collections = GC.CollectionCount(0),
        GcGen1Collections = GC.CollectionCount(1),
        GcGen2Collections = GC.CollectionCount(2),
        GcTotalMemory = GC.GetTotalMemory(false),
        PinnedObjects = _runtime?.PinnedObjectCount ?? 0,
        DispatcherResponsivenessMs = Math.Round(dispatcherMs, 2),
        Gen0HeapSize = gen0,
        Gen1HeapSize = gen1,
        Gen2HeapSize = gen2,
        LohSize = loh,
      }
    });
  }
}
