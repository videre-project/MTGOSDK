/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.Core.Memory;

/// <summary>
/// Manages garbage collection (GC) notifications and manages scheduling for
/// object cleanup for managed objects that require synchronization with GC.
/// </summary>
public static class GCTimer
{
  public sealed class GCTimerPause : IDisposable
  {
    private bool _disposed = false;

    public GCTimerPause() => Stop();

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;

      // Stop the timer and cancel any GC notifications
      Stop();
      GC.SuppressFinalize(this);
    }
  }

  public static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromSeconds(30);

  private static readonly ConcurrentQueue<IObjectReference> s_objQueue = new();
  private static Timer s_timer;
  private static readonly CancellationTokenSource s_gcListenerCts = new();
  private static readonly Task s_gcListenerTask;
  private static bool s_gcNotificationsAvailable = false;
  private static bool s_noGCRegionActive = false;
  private static readonly ReaderWriterLockSlim s_gcRegionLock = new();

  /// Starts the GC timer and enables GC notifications by default.
  static GCTimer()
  {
    try
    {
      GC.RegisterForFullGCNotification(80, 80);
      s_gcNotificationsAvailable = true;
      s_gcListenerTask = Task.Factory.StartNew(
        GCListenerLoop,
        s_gcListenerCts.Token,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default
      ).Unwrap();
    }
    catch
    {
      // Fallback: If notification registration fails, use a timer instead.
      UnsubscribeFromGCNotifications();
    }
  }

  private static async Task GCListenerLoop()
  {
    while (!s_gcListenerCts.Token.IsCancellationRequested)
    {
      var status = GC.WaitForFullGCApproach(1000);
      if (status == GCNotificationStatus.Succeeded)
      {
        GC.WaitForFullGCComplete();
        GCCallback(null);
      }
      await Task.Delay(1000, s_gcListenerCts.Token).ConfigureAwait(false);
    }
  }

  private static void GCCallback(object? state)
  {
    while (s_objQueue.TryDequeue(out IObjectReference objRef))
    {
      // Object has been GC'd, release its reference count using the stored ref.
      objRef?.ReleaseReference(false);
    }
  }

  /// <summary>
  /// Enqueues an object reference for cleanup when the garbage collector runs.
  /// </summary>
  /// <remarks>
  /// This is useful for synchronizing cleanup of any unmanaged resources to
  /// ensure cleanup occurs when there are no more managed references to it.
  /// </remarks>
  public static void Enqueue(IObjectReference objRef)
  {
    if (objRef == null || !objRef.IsValid)
      return;

    s_objQueue.Enqueue(objRef);
  }

  /// <summary>
  /// Starts the garbage collection timer and enables GC notifications if available.
  /// </summary>
  public static void Start()
  {
    // Only end NoGCRegion if it is active
    s_gcRegionLock.EnterUpgradeableReadLock();
    try
    {
      if (s_noGCRegionActive)
      {
        s_gcRegionLock.EnterWriteLock();
        try
        {
          if (s_noGCRegionActive &&
              Try<bool>(() => { GC.EndNoGCRegion(); return true; }))
          {
            s_noGCRegionActive = false;
          }
        }
        finally { s_gcRegionLock.ExitWriteLock(); }
      }
    }
    finally { s_gcRegionLock.ExitUpgradeableReadLock(); }

    // If GC notifications are available, the timer is already running
    if (s_gcNotificationsAvailable) return;

    if (s_timer == null)
    {
      s_timer = new(GCCallback, null, TimeSpan.Zero, DefaultCleanupInterval);
    }
    else
    {
      s_timer.Change(TimeSpan.Zero, DefaultCleanupInterval);
    }
  }

  /// <summary>
  /// Stops the garbage collection timer and cancels any GC notifications.
  /// /// </summary>
  public static void Stop()
  {
    // Only start NoGCRegion if it is not active
    s_gcRegionLock.EnterUpgradeableReadLock();
    try
    {
      if (!s_noGCRegionActive)
      {
        s_gcRegionLock.EnterWriteLock();
        try
        {
          if (!s_noGCRegionActive &&
              Try(() => GC.TryStartNoGCRegion(128 * 1024 * 1024)))
          {
            s_noGCRegionActive = true;
          }
        }
        finally { s_gcRegionLock.ExitWriteLock(); }
      }
    }
    finally { s_gcRegionLock.ExitUpgradeableReadLock(); }

    if (s_gcNotificationsAvailable)
    {
      s_gcListenerCts.Cancel();
    }
    s_timer?.Change(Timeout.Infinite, Timeout.Infinite);
  }

  /// <summary>
  /// Suppresses garbage collection for the duration of the IDisposable scope.
  /// </summary>
  /// <remarks>
  /// This creates a no-GC region that requests the garbage collector to suspend
  /// activity until the scope is disposed. This also pauses any GC handling of
  /// remote objects until the scope is disposed.
  /// </remarks>
  public static IDisposable SuppressGC() => new GCTimerPause();

  /// <summary>
  /// Unsubscribes from GC notifications and uses the timer to manage cleanup.
  /// </summary>
  /// <remarks>
  /// This method is useful when you want to stop receiving GC notifications
  /// and have a more controlled cleanup process using the timer.
  /// <para/>
  /// This is useful in environments using server GC, where GC notifications are
  /// not frequently available or reliable, which may cause memory fragmentation
  /// in the MTGO client from holding onto too many pinned objects for too long.
  /// </remarks>
  public static void UnsubscribeFromGCNotifications()
  {
    if (s_gcNotificationsAvailable)
    {
      GC.CancelFullGCNotification();
      s_gcNotificationsAvailable = false;
      s_gcListenerCts.Cancel();
      s_timer = new Timer(GCCallback, null, Timeout.Infinite, Timeout.Infinite);
    }
  }
}
