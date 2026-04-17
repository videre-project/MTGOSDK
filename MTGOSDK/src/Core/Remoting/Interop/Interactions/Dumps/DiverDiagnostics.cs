/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class DiverDiagnostics
{
  [Key(0)]
  public int SyncThreadActive { get; set; }

  [Key(1)]
  public int SyncThreadQueued { get; set; }

  [Key(2)]
  public int SyncThreadMax { get; set; }

  [Key(3)]
  public int ActiveHooks { get; set; }

  [Key(4)]
  public int ActiveEventSubscriptions { get; set; }

  [Key(5)]
  public int ConnectedClients { get; set; }

  [Key(6)]
  public Dictionary<string, EndpointStats> Endpoints { get; set; }

  [Key(7)]
  public long CallbacksSent { get; set; }

  [Key(8)]
  public double LastCallbackQueueDelayMs { get; set; }

  [Key(9)]
  public StaThreadStats StaThread { get; set; }

  /// <summary>
  /// Host process health snapshot — ThreadPool availability and GC pressure.
  /// </summary>
  [Key(10)]
  public HostProcessStats HostProcess { get; set; }
}

[MessagePackObject]
public class EndpointStats
{
  [Key(0)]
  public long Count { get; set; }

  [Key(1)]
  public double AvgMs { get; set; }

  [Key(2)]
  public double LastMs { get; set; }
}

[MessagePackObject]
public class StaThreadStats
{
  [Key(0)]
  public long TotalOps { get; set; }

  [Key(1)]
  public long DispatcherOps { get; set; }

  [Key(2)]
  public int PendingOps { get; set; }

  [Key(3)]
  public double AvgDispatchMs { get; set; }

  [Key(4)]
  public long Timeouts { get; set; }
}

/// <summary>
/// Snapshot of the host process's .NET runtime health.
/// </summary>
[MessagePackObject]
public class HostProcessStats
{
  /// <summary>
  /// Available worker threads in the host process's ThreadPool.
  /// A value near zero indicates ThreadPool starvation.
  /// </summary>
  [Key(0)]
  public int ThreadPoolAvailableWorkers { get; set; }

  /// <summary>
  /// Maximum worker threads in the host process's ThreadPool.
  /// </summary>
  [Key(1)]
  public int ThreadPoolMaxWorkers { get; set; }

  /// <summary>
  /// Available I/O completion port threads.
  /// </summary>
  [Key(2)]
  public int ThreadPoolAvailableIO { get; set; }

  /// <summary>
  /// Maximum I/O completion port threads.
  /// </summary>
  [Key(3)]
  public int ThreadPoolMaxIO { get; set; }

  /// <summary>
  /// Cumulative Gen0 garbage collections since process start.
  /// </summary>
  [Key(4)]
  public int GcGen0Collections { get; set; }

  /// <summary>
  /// Cumulative Gen1 garbage collections since process start.
  /// </summary>
  [Key(5)]
  public int GcGen1Collections { get; set; }

  /// <summary>
  /// Cumulative Gen2 (full) garbage collections since process start.
  /// A spike in Gen2 during the freeze indicates GC pressure.
  /// </summary>
  [Key(6)]
  public int GcGen2Collections { get; set; }

  /// <summary>
  /// Total managed heap size in bytes.
  /// </summary>
  [Key(7)]
  public long GcTotalMemory { get; set; }

  /// <summary>
  /// Number of objects pinned by the Diver's SnapshotRuntime.
  /// </summary>
  [Key(8)]
  public int PinnedObjects { get; set; }

  /// <summary>
  /// Milliseconds the WPF Dispatcher took to execute a no-op probe.
  /// High values indicate the UI thread is blocked or starved.
  /// -1 if the Dispatcher is unavailable.
  /// </summary>
  [Key(9)]
  public double DispatcherResponsivenessMs { get; set; }

  /// <summary>Gen 0 heap size in bytes.</summary>
  [Key(10)]
  public long Gen0HeapSize { get; set; }

  /// <summary>Gen 1 heap size in bytes.</summary>
  [Key(11)]
  public long Gen1HeapSize { get; set; }

  /// <summary>Gen 2 heap size in bytes.</summary>
  [Key(12)]
  public long Gen2HeapSize { get; set; }

  /// <summary>Large Object Heap size in bytes.</summary>
  [Key(13)]
  public long LohSize { get; set; }
}
