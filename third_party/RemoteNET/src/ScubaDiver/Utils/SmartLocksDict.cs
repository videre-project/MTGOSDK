using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;


namespace ScubaDiver;

/// <summary>
/// This collection allows saving a "lock" for every element where:
/// 1. Multiple threads can lock the same element at the same time
/// 2. Notify a thread if it tries to lock an element which it already locked.
/// 3. Also there's an API to temporarily block threads for blocking ANY element.
/// </summary>
public class SmartLocksDict<T>
{
  private ConcurrentDictionary<int, SmartLockThreadState> _threadStates = new();

  public class Entry
  {
    public object _lock = new();
    public HashSet<int> _holdersThreadIDs = new();
  }

  private ConcurrentDictionary<T, Entry> _dict = new();

  [Flags]
  public enum SmartLockThreadState
  {
    // Default state, if others aren't defined this one is implied
    AllowAllLocks = 0,
    // This thread is not allowed to lock any of the locks
    ForbidLocking,
    // When combined with ForbidLocking, it means the thread is GENERALLY not
    // allowed to lock but temporarily it is.
    TemporarilyAllowLocks
  }

  public void SetSpecialThreadState(int tid, SmartLockThreadState state)
  {
    if (_threadStates.TryGetValue(tid, out var current))
    {
      if (state == SmartLockThreadState.AllowAllLocks)
        _threadStates.TryRemove(tid, out _);
      else
        _threadStates.TryUpdate(tid, state, current);
    }
    else
    {
      _threadStates.TryAdd(tid, state);
    }
  }

  public void Add(T item) => _dict.TryAdd(item, new Entry());

  public void Remove(T item) => _dict.TryRemove(item, out _);

  public enum AcquireResults
  {
    NoSuchItem,
    Acquired,
    AlreadyAcquireByCurrentThread,
    ThreadNotAllowedToLock
  }

  public AcquireResults Acquire(T item)
  {
    int currentThreadId = Thread.CurrentThread.ManagedThreadId;
    if (_threadStates.TryGetValue(currentThreadId, out SmartLockThreadState current))
    {
      if (current.HasFlag(SmartLockThreadState.ForbidLocking))
      {
        if (!current.HasFlag(SmartLockThreadState.TemporarilyAllowLocks))
        {
          return AcquireResults.ThreadNotAllowedToLock;
        }
      }
    }

    if (!_dict.TryGetValue(item, out Entry entry))
      return AcquireResults.NoSuchItem;

    AcquireResults result;
    lock (entry._lock)
    {
      if (entry._holdersThreadIDs.Contains(currentThreadId))
      {
        result = AcquireResults.AlreadyAcquireByCurrentThread;
      }
      else
      {
        entry._holdersThreadIDs.Add(currentThreadId);
        result = AcquireResults.Acquired;
      }
    }

    return result;
  }

  public void Release(T item)
  {
    if (!_dict.TryGetValue(item, out Entry entry))
      return;

    lock (entry._lock)
    {
      int currentThreadId = Thread.CurrentThread.ManagedThreadId;
      entry._holdersThreadIDs.Remove(currentThreadId);
    }
  }
}
