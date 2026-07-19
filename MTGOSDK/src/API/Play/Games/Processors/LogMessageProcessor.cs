/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Users;
using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using WotC.MtGO.Client.Model.Chat;
using ChannelManager = MTGOSDK.API.Chat.ChannelManager;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Correlates game log messages with state snapshots by finding the closest
/// temporal match within a sliding window of recent snapshots.
/// </summary>
/// <remarks>
/// <para>
/// Log messages arrive via the game's log channel with a <c>DateTime</c>
/// timestamp in local client time. Game state snapshots have an
/// <see cref="GameStateSnapshot.ActionTimestamp"/> in server time.
/// </para>
/// <para>
/// This processor converts snapshot timestamps to client time using
/// <see cref="ServerTime.ServerTimeAsClientTime"/> before comparing.
/// It maintains a sliding window of recent snapshots and correlates each
/// log message with the snapshot whose converted <c>ActionTimestamp</c> is
/// closest to the message's <c>Timestamp</c>.
/// </para>
/// </remarks>
public sealed class LogMessageProcessor : IGameStateProcessor
{
  private Game? _game;
  private Channel? _channel;
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
  /// Sliding window of recent snapshots, ordered by ActionTimestamp.
  /// </summary>
  private readonly List<GameStateSnapshot> _snapshotWindow = new();

  /// <summary>
  /// The most recent snapshot received from the processor pipeline.
  /// </summary>
  public GameStateSnapshot? LastSnapshot { get; private set; }

  private GameContext? _lastContext;

  /// <summary>
  /// Buffer of uncorrelated log messages waiting for a matching snapshot.
  /// </summary>
  private readonly List<Message> _pendingMessages = new();

  /// <summary>
  /// Tracks messages already emitted to prevent duplicates from the real-time
  /// hook and historical polling processing the same message.
  /// </summary>
  private readonly HashSet<(string? User, DateTime Timestamp, string Text)>
    _emittedMessages = new();

  public void Initialize(Game game)
  {
    _game = game;
    _channel = game.LogChannel;

    // Subscribe to static event with a filter that matches the game's log channel
    _localFileName = _channel.LocalFileName;
    Channel.MessageReceived += OnLogMessageReceived;

    // Clear any pending messages that might have arrived during subscription
    _pendingMessages.Clear();

    // Start asynchronous polling for historical messages in the background.
    // This avoids blocking initialization and allows the processor to start
    // handling real-time messages immediately.
    var pollTask = Task.Run(async () =>
    {
      var gameStartTime = game.StartTime;
      var pollEndTime = DateTime.UtcNow.AddSeconds(10);
      var processedMessageKeys = new HashSet<(string? User, DateTime Timestamp, string Text)>();
      var historicalCount = 0;

      while (DateTime.UtcNow < pollEndTime)
      {
        foreach (var msg in _channel.Messages)
        {
          var key = (msg.User?.Id.ToString() ?? "system", msg.Timestamp, msg.Text);

          if (processedMessageKeys.Contains(key))
            continue;

          if (msg.Timestamp >= gameStartTime.AddSeconds(-5))
          {
            processedMessageKeys.Add(key);
            historicalCount++;
            OnLogMessageReceived(_channel, msg);
          }
        }

        await Task.Delay(100);
      }
    });
  }

  public void Process(GameContext context)
  {
    _lastContext = context;
    LastSnapshot = context.Current;

    // Add snapshot to sliding window
    _snapshotWindow.Add(LastSnapshot);

    // Trim window to max size, removing oldest entries
    while (_snapshotWindow.Count > MaxSnapshotWindow)
      _snapshotWindow.RemoveAt(0);

    // Try to correlate all pending messages
    TryCorrelatePending(context);
  }

  private void OnLogMessageReceived(Channel source, Message message)
  {
    // Check if the originating channel is parented to the game log channel.
    // If so, it's a valid log message for the current game.
    if (source.LocalFileName != _localFileName) return;

    // Dedup: both the real-time hook and historical polling can deliver the
    // same message. Skip if we've already emitted or buffered this one.
    var key = (message.User?.Name, message.Timestamp, message.Text);
    lock (_emittedMessages)
    {
      if (!_emittedMessages.Add(key)) return;
    }

    // Always buffer — don't correlate immediately. During rapid state
    // transitions (e.g., combat phases), the message often arrives before
    // its associated snapshot enters the window. Deferring correlation to
    // the next Process() call ensures the window contains the correct
    // snapshot for "closest" matching.
    _pendingMessages.Add(message);
  }

  /// <summary>
  /// Attempts to correlate all pending messages with available snapshots.
  /// </summary>
  private void TryCorrelatePending(GameContext context)
  {
    var correlated = new List<Message>();

    foreach (var msg in _pendingMessages)
    {
      if (TryCorrelateMessage(msg, out var snapshot, out var delta))
      {
        correlated.Add(msg);
        context.Emit(
          new LogMessageCorrelatedEventArgs(snapshot, msg, delta));
      }
    }

    // Remove correlated messages from the pending buffer
    foreach (var msg in correlated)
      _pendingMessages.Remove(msg);

    // Flush stale messages that are too old to ever correlate
    FlushStaleMessages();
  }

  /// <summary>
  /// Removes pending messages that are too old to ever correlate with the
  /// current snapshot window (older than oldest snapshot + MaxCorrelationDeltaSeconds).
  /// </summary>
  private void FlushStaleMessages()
  {
    if (_snapshotWindow.Count == 0 || _pendingMessages.Count == 0)
      return;

    var oldestSnapshot = _snapshotWindow.First();
    var cutoffTime = oldestSnapshot.ClientTimestamp.AddSeconds(-MaxCorrelationDeltaSeconds);

    var staleMessages = _pendingMessages.Where(msg =>
    {
      var msgTs = Unbind(msg).__timestamp;
      return msgTs < cutoffTime;
    }).ToList();

    foreach (var msg in staleMessages)
    {
      _pendingMessages.Remove(msg);
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

    // Both use the same client-side __timestamp calculation
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

  public void Dispose()
  {
    Channel.MessageReceived -= OnLogMessageReceived;
    _pendingMessages.Clear();
    _snapshotWindow.Clear();
    _emittedMessages.Clear();
  }

  /// <summary>
  /// Ensures the static <c>OnLogMessage</c> hook is installed
  /// so that log message detection is active.
  /// </summary>
  public static void EnsureHookInitialized() =>
    Channel.MessageReceived.EnsureInitialize();
}
