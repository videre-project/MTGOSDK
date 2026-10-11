/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace ScubaDiver.Hooking;

/// <summary>
/// Groups synchronous notifications for a native owner and defers their projection
/// until the enclosing invocation completes.
/// </summary>
internal sealed class CaptureScopeManager : IDisposable
{
  private sealed class Stream
  {
    public readonly object Lock = new();
    public readonly Guid Id = Guid.NewGuid();
    public int Subscribers;
    public volatile bool Retired;
    public long Sequence;
    public long Revision;
    public readonly ConditionalWeakTable<object, RootRegistration> Roots = new();
  }

  private sealed class RootRegistration
  {
    public int Subscribers;
  }

  private sealed class Scope(string group, object owner, Stream stream,
    Action<RemoteCaptureSequence, Exception> complete, bool stateBoundary, long revision)
  {
    public readonly string Group = group;
    public readonly object Owner = owner;
    public readonly Stream Stream = stream;
    public readonly Action<RemoteCaptureSequence, Exception> Complete = complete;
    public readonly bool IsStateBoundary = stateBoundary;
    public readonly long Revision = revision;
    public Action<RemoteCaptureSequence, Exception> NotificationCapture;
    public readonly List<Action<RemoteCaptureSequence, Exception>> BoundaryCallbacks = new();
    public int Depth;
    public bool Changed;
    public bool Completing;
    public int Notifications;
    public readonly HashSet<string> Events = new(StringComparer.Ordinal);
    public readonly Dictionary<string, HashSet<ulong>> EventSources = new(StringComparer.Ordinal);
  }

  private sealed class Subscription(Action release) : IDisposable
  {
    private Action _release = release;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
  }

  private readonly ConditionalWeakTable<object, ConcurrentDictionary<string, Stream>> _streams = new();
  private readonly ThreadLocal<List<Scope>> _active = new(() => new());

  //
  // Owner registration and stream lifetime.
  //

  /// <summary>
  /// Validates owner binding before installing a capture subscription or patch.
  /// </summary>
  public static void Validate(RemoteCaptureScope scope, RemoteBatchRequest batch)
  {
    if (scope == null) return;
    if (string.IsNullOrWhiteSpace(scope.Group) || scope.OwnerInput < 0
      || scope.OwnerInput >= batch.Inputs.Count
      || (scope.OwnerPath != null && scope.OwnerPath.Split('.').Any(string.IsNullOrWhiteSpace)))
      throw new ArgumentException("Invalid capture scope group or owner binding.");
  }

  /// <summary>
  /// Retains a stream for the lifetime of an enrolled native subscription.
  /// </summary>
  public IDisposable Subscribe(string group, object owner, out RemoteCaptureSequence baseline,
    object captureRoot = null)
  {
    if (owner == null) throw new ArgumentNullException(nameof(owner));
    if (owner.GetType().IsValueType)
      throw new ArgumentException("Capture owner must have native reference identity.", nameof(owner));
    var groups = _streams.GetOrCreateValue(owner);
    while (true)
    {
      var stream = groups.GetOrAdd(group, _ => new());
      RootRegistration root = null;
      lock (stream.Lock)
      {
        if (stream.Retired) continue;
        ++stream.Subscribers;
        if (captureRoot != null)
        {
          root = stream.Roots.GetValue(captureRoot, _ => new());
          Interlocked.Increment(ref root.Subscribers);
        }
        baseline = new()
        {
          StreamId = stream.Id,
          Sequence = stream.Sequence,
          StateRevision = stream.Revision
        };
      }
      return new Subscription(() =>
      {
        lock (stream.Lock)
        {
          if (root != null) Interlocked.Decrement(ref root.Subscribers);
          if (--stream.Subscribers != 0) return;
          stream.Retired = true;
          groups.TryRemove(group, out _);
        }
      });
    }
  }

  private Stream FindStream(string group, object owner) =>
    owner != null && _streams.TryGetValue(owner, out var groups)
      && groups.TryGetValue(group, out var stream) ? stream : null;

  public bool HasSubscribers(string group, object owner) => FindStream(group, owner) != null;

  /// <summary>
  /// Checks that the filtered capture root still has an enrolled subscription.
  /// Replaced roots stop producing captures without examining their child objects.
  /// </summary>
  public bool HasSubscribers(string group, object owner, object captureRoot) =>
    FindStream(group, owner) is { } stream && captureRoot != null
      && stream.Roots.TryGetValue(captureRoot, out var root)
      && Volatile.Read(ref root.Subscribers) > 0;

  //
  // Native invocation scopes and retained notification identities.
  //

  private Scope FindActive(string group, object owner)
  {
    var active = _active.Value;
    for (int index = active.Count - 1; index >= 0; --index)
      if (!active[index].Stream.Retired && active[index].Group == group
        && ReferenceEquals(active[index].Owner, owner))
        return active[index];
    return null;
  }

  /// <summary>
  /// Returns an invocation-local exit callback for Harmony's __state.
  /// Nested calls share a scope only on the same native thread and owner.
  /// </summary>
  public Action<Exception> Enter(string group, object owner, bool changed,
    Action<RemoteCaptureSequence, Exception> complete, bool stateBoundary = false)
  {
    var scope = FindActive(group, owner);
    if (scope == null || (stateBoundary && !scope.IsStateBoundary))
    {
      var stream = FindStream(group, owner);
      if (stream == null) return null;
      lock (stream.Lock)
        scope = new(group, owner, stream, complete, stateBoundary,
          stateBoundary ? ++stream.Revision : stream.Revision);
      _active.Value.Add(scope);
    }
    if (scope.Completing) return null;

    // Several registrations can enroll the same invocation. Each boundary subscriber
    // needs its own completion callback, while notification projections remain shared.
    if (stateBoundary) scope.BoundaryCallbacks.Add(complete);
    if (scope.IsStateBoundary && !stateBoundary)
      scope.NotificationCapture ??= complete;

    ++scope.Depth;
    scope.Changed |= changed;
    var entered = scope;
    // Decrement this invocation's scope depth only once, even if exit is called again.
    int exited = 0;
    return error =>
    {
      if (Interlocked.Exchange(ref exited, 1) == 0) Exit(entered, error);
    };
  }

  /// <summary>
  /// Marks the enclosing update without executing its projection or queueing IPC.
  /// </summary>
  public bool MarkChanged(string group, object owner, string eventName,
    Action<RemoteCaptureSequence, Exception> capture = null, ulong sourceToken = 0)
  {
    var scope = FindActive(group, owner);
    if (scope == null) return false;
    if (scope.IsStateBoundary) scope.NotificationCapture ??= capture;
    scope.Changed = true;
    ++scope.Notifications;
    scope.Events.Add(eventName);
    if (sourceToken != 0)
    {
      if (!scope.EventSources.TryGetValue(eventName, out var sources))
      {
        sources = new();
        scope.EventSources.Add(eventName, sources);
      }
      sources.Add(sourceToken);
    }
    return true;
  }

  //
  // Ordered capture completion and detached metadata.
  //

  private void Exit(Scope scope, Exception error)
  {
    if (--scope.Depth != 0) return;
    scope.Completing = true;
    try
    {
      if (!scope.Changed && error == null) return;

      lock (scope.Stream.Lock)
      {
        // Capture the grouped notifications first so boundary callbacks can link
        // to that exact capture through NotificationSequence.
        long notificationSequence = 0;
        Exception captureError = error;
        try
        {
          if (scope.NotificationCapture != null)
            Complete(scope.Stream, true, sequence =>
            {
              notificationSequence = sequence.Sequence;
              SetScopeMetadata(sequence, scope);
              scope.NotificationCapture(sequence, error);
            }, scope.Revision);
        }
        catch (Exception failure)
        {
          captureError = failure;
        }

        void CompleteCallback(Action<RemoteCaptureSequence, Exception> callback) =>
          Complete(scope.Stream, true, sequence =>
          {
            SetScopeMetadata(sequence, scope);
            sequence.IsStateBoundary = scope.IsStateBoundary;
            sequence.NotificationSequence = notificationSequence;
            callback(sequence, captureError);
          }, scope.IsStateBoundary ? scope.Revision : scope.Stream.Revision);

        if (scope.IsStateBoundary)
        {
          foreach (var callback in scope.BoundaryCallbacks)
            CompleteCallback(callback);
        }
        else
        {
          CompleteCallback(scope.Complete);
        }
      }
    }
    finally
    {
      // Keep the scope active while projecting values because a native getter
      // may raise another notification.
      _active.Value.Remove(scope);
    }
  }

  private static void SetScopeMetadata(RemoteCaptureSequence sequence, Scope scope)
  {
    sequence.NotificationCount = scope.Notifications;
    sequence.EventNames = scope.Events.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    sequence.EventSources = scope.EventSources.ToDictionary(entry => entry.Key,
      entry => entry.Value.OrderBy(token => token).ToArray(), StringComparer.Ordinal);
  }

  /// <summary>
  /// Sequences a standalone notification in the owner's same completion stream.
  /// </summary>
  public void Complete(string group, object owner, Action<RemoteCaptureSequence> complete)
  {
    var stream = FindStream(group, owner);
    if (stream != null) Complete(stream, false, complete);
  }

  private static void Complete(Stream stream, bool scoped,
    Action<RemoteCaptureSequence> complete, long? revision = null)
  {
    lock (stream.Lock)
    {
      if (stream.Subscribers == 0) return;
      var sequence = new RemoteCaptureSequence
      {
        StreamId = stream.Id,
        Sequence = ++stream.Sequence,
        Scoped = scoped,
        StateRevision = revision ?? stream.Revision
      };
      // Assign the sequence and queue delivery under the stream lock so another
      // thread cannot enqueue a later capture ahead of this one.
      complete(sequence);
    }
  }

  public void Dispose() => _active.Dispose();
}
