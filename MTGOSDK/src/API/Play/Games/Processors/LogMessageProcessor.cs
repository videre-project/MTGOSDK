/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Correlates game log messages with state snapshots by finding the closest
/// temporal match within a sliding window of recent snapshots.
/// </summary>
/// <remarks>
/// Correlation compares client receive timestamps for messages and snapshots.
/// Messages wait for the next processing tick so newly arrived snapshots can
/// participate in the closest-time match. This is temporal association, not
/// the prompt-identity correlation performed by PromptProcessor.
/// </remarks>
public sealed class LogMessageProcessor : IGameStateProcessor, IDisposable
{
  private readonly object _messageLock = new();
  private volatile bool _disposed;
  private string _localFileName = null!;

  /// <summary>
  /// Maximum number of snapshots to keep in the sliding window.
  /// </summary>
  private const int MaxSnapshotWindow = 6;

  /// <summary>
  /// Maximum time delta (in seconds) for a valid correlation.
  /// </summary>
  private const double MaxCorrelationDeltaSeconds = 5;

  /// <summary>
  /// Sliding window of recent snapshots, in processing order.
  /// </summary>
  private readonly List<GameStateSnapshot> _snapshotWindow = new();

  /// <summary>
  /// The most recent snapshot received from the processor pipeline.
  /// </summary>
  public GameStateSnapshot? LastSnapshot { get; private set; }

  /// <summary>
  /// Buffer of uncorrelated log messages waiting for a matching snapshot.
  /// </summary>
  private readonly List<Message> _pendingMessages = new();

  /// <summary>
  /// Tracks buffered or emitted messages to prevent duplicates from the real-time
  /// hook and historical polling processing the same message.
  /// </summary>
  private readonly HashSet<(string? User, DateTime Timestamp, string Text)>
    _seenMessages = new();

  //
  // Subscription and history capture
  //

  public void Initialize(Game game)
  {
    ArgumentNullException.ThrowIfNull(game);
    ObjectDisposedException.ThrowIf(_disposed, this);
    var channel = game.LogChannel;
    _localFileName = channel.LocalFileName;
    Channel.MessageReceived += OnLogMessageReceived;

    // Read existing channel history while real-time notifications remain active.
    // Both paths use the same deduplication and pending-message lock.
    _ = Task.Run(() => ReadHistoricalMessagesAsync(game, channel));
  }

  private async Task ReadHistoricalMessagesAsync(Game game, Channel channel)
  {
    try
    {
      if (_disposed)
        return;
      var earliestMessage = game.StartTime.AddSeconds(-5);
      var pollEndTime = DateTime.UtcNow.AddSeconds(10);
      var processedMessageKeys = new HashSet<(string? User, DateTime Timestamp, string Text)>();

      while (!_disposed && DateTime.UtcNow < pollEndTime)
      {
        foreach (var message in channel.Messages)
        {
          if (_disposed)
            return;
          var key = (message.User?.Id.ToString() ?? "system", message.Timestamp, message.Text);
          if (message.Timestamp >= earliestMessage && processedMessageKeys.Add(key))
            OnLogMessageReceived(channel, message);
        }
        await Task.Delay(100).ConfigureAwait(false);
      }
    }
    catch (Exception error)
    {
      // A disposed scope no longer needs history; other failures remain visible.
      if (!_disposed) Log.Error(error, "Failed to read historical game log messages.");
    }
  }

  //
  // Snapshot correlation
  //

  public void Process(GameContext context)
  {
    if (_disposed)
      return;
    LastSnapshot = context.Current;

    _snapshotWindow.Add(LastSnapshot);

    while (_snapshotWindow.Count > MaxSnapshotWindow)
      _snapshotWindow.RemoveAt(0);

    TryCorrelatePending(context);
  }

  private void OnLogMessageReceived(Channel source, Message message)
  {
    // Match the originating channel to this game's log file.
    if (_disposed || source.LocalFileName != _localFileName)
      return;

    // History and native notifications can deliver the same message.
    var key = (message.User?.Name, message.Timestamp, message.Text);
    lock (_messageLock)
    {
      if (_disposed || !_seenMessages.Add(key))
        return;
      _pendingMessages.Add(message);
    }
  }

  /// <summary>
  /// Attempts to correlate all pending messages with available snapshots.
  /// </summary>
  private void TryCorrelatePending(GameContext context)
  {
    List<Message> pending;
    lock (_messageLock)
    {
      pending = new List<Message>(_pendingMessages);
      _pendingMessages.Clear();
    }

    // Call subscribers outside the message lock. New arrivals stay in the buffer
    // for the next tick instead of changing this enumeration.
    try
    {
      foreach (var message in pending.ToArray())
      {
        if (TryCorrelateMessage(message, out var snapshot, out var delta))
        {
          context.Emit(new LogMessageCorrelatedEventArgs(snapshot, message, delta));
          pending.Remove(message);
        }
      }
      FlushStaleMessages(pending);
    }
    finally
    {
      // Preserve unmatched messages if a timestamp read or correlation fails.
      lock (_messageLock)
        if (!_disposed)
          _pendingMessages.InsertRange(0, pending);
    }
  }

  /// <summary>
  /// Removes messages older than the first retained snapshot's correlation window.
  /// </summary>
  private void FlushStaleMessages(List<Message> pending)
  {
    if (_snapshotWindow.Count == 0 || pending.Count == 0)
      return;

    var oldestSnapshot = _snapshotWindow[0];
    var cutoffTime = oldestSnapshot.ClientTimestamp.AddSeconds(-MaxCorrelationDeltaSeconds);

    var staleMessages = pending.Where(msg =>
    {
      var msgTs = Unbind(msg).__timestamp;
      return msgTs < cutoffTime;
    }).ToList();

    foreach (var msg in staleMessages)
    {
      pending.Remove(msg);
    }
  }

  /// <summary>
  /// Finds the snapshot closest to the given message's timestamp.
  /// </summary>
  /// <param name="message">The log message to correlate.</param>
  /// <param name="bestSnapshot">The closest matching snapshot, if found.</param>
  /// <param name="bestDelta">The time delta in seconds between the message and snapshot.</param>
  /// <returns>True if a valid correlation was found within the max delta threshold.</returns>
  private bool TryCorrelateMessage(
    Message message,
    out GameStateSnapshot bestSnapshot,
    out double bestDelta)
  {
    bestSnapshot = null!;
    bestDelta = double.MaxValue;

    // Compare receive times, rather than the timestamp displayed in the log.
    var msgClientTs = Unbind(message).__timestamp;

    foreach (var snapshot in _snapshotWindow)
    {
      var delta = Math.Abs((snapshot.ClientTimestamp - msgClientTs).TotalSeconds);
      if (delta < bestDelta)
      {
        bestDelta = delta;
        bestSnapshot = snapshot;
      }
    }

    return bestSnapshot != null && bestDelta <= MaxCorrelationDeltaSeconds;
  }

  //
  // Lifetime and hook initialization
  //

  public void Dispose()
  {
    lock (_messageLock)
    {
      if (_disposed)
        return;
      _disposed = true;
      _pendingMessages.Clear();
      _seenMessages.Clear();
    }
    Channel.MessageReceived -= OnLogMessageReceived;
    _snapshotWindow.Clear();
  }

  /// <summary>
  /// Ensures the static <c>OnLogMessage</c> hook is installed
  /// so that log message detection is active.
  /// </summary>
  public static void EnsureHookInitialized() =>
    Channel.MessageReceived.EnsureInitialize();
}
