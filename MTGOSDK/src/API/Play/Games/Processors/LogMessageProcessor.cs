/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Intermediate;
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
  private int _gameId;

  private sealed record PendingMessage(Message Message, DateTime Timestamp, bool Historical);

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
  private readonly List<PendingMessage> _pendingMessages = new();

  /// <summary>
  /// Tracks buffered or emitted messages to prevent duplicates from the real-time
  /// hook and historical backfill processing the same message.
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
    _gameId = game.Id;
    Channel.MessageReceived += OnLogMessageReceived;

    // MTGO loads existing channel history synchronously during channel creation.
    foreach (var message in ReadHistory(game, channel.Messages.ToArray()))
      BufferMessage(message, historical: true);
  }

  /// <summary>
  /// Resolves missing game openings from the native channel's existing history.
  /// Called once by GameProcessor before any snapshots or persistence begin.
  /// </summary>
  internal static void InitializeStartTime(Game game)
  {
    int gameId = game.Id;
    var games = game.Match.Games.Select(item => item.Id).ToArray();
    int index = Array.IndexOf(games, gameId);
    if (index < 0) return;

    bool hasStart = GameProcessor.TryGetStartTime(gameId, out var start);
    bool needsNext = index + 1 < games.Length &&
      !GameProcessor.TryGetStartTime(games[index + 1], out _);
    if (!hasStart || needsNext)
    {
      var messages = game.LogChannel.Messages.Select(message => new GameLogHistory.Entry(
        message.Timestamp.ToUniversalTime(), message.Text)).ToArray();
      var ranges = GameLogHistory.Split(messages,
        game.Players.Select(player => player.Name).ToArray());
      // Ordinal mapping requires history for every game in this match.
      if (ranges.Count == games.Length)
        for (int i = 0; i < games.Length; i++)
          GameProcessor.RememberStartTime(games[i], messages[ranges[i].Start].Timestamp);
    }
    if (GameProcessor.TryGetStartTime(gameId, out start))
      game.StartTime = start;
  }

  /// <summary>
  /// Selects messages using the boundaries established during initialization.
  /// An unknown boundary never admits earlier games' history.
  /// </summary>
  private static Message[] ReadHistory(Game game, Message[] messages)
  {
    if (game.IsReplay) return messages;
    if (!GameProcessor.TryGetStartTime(game.Id, out var start)) return [];
    var games = game.Match.Games.Select(item => item.Id).ToArray();
    int index = Array.IndexOf(games, game.Id);
    if (index < 0) return [];
    DateTime? end = null;
    if (index + 1 < games.Length)
    {
      if (!GameProcessor.TryGetStartTime(games[index + 1], out var nextStart)) return [];
      end = nextStart;
    }
    return messages.Where(message =>
    {
      var timestamp = message.Timestamp.ToUniversalTime();
      return timestamp >= start && (end is null || timestamp < end);
    }).ToArray();
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

    // Live channels identify their originating game. Historical messages are
    // filtered separately because each game channel loads the shared match log.
    if ((int)Unbind(source).Game.Id != _gameId) return;
    BufferMessage(message, historical: false);
  }

  private void BufferMessage(Message message, bool historical)
  {
    // History and native notifications can deliver the same message.
    var timestamp = message.Timestamp.ToUniversalTime();
    var key = (message.User?.Name, timestamp, message.Text);
    lock (_messageLock)
    {
      if (_disposed || !_seenMessages.Add(key))
        return;
      _pendingMessages.Add(new(message, timestamp, historical));
    }
  }

  /// <summary>
  /// Attempts to correlate all pending messages with available snapshots.
  /// </summary>
  private void TryCorrelatePending(GameContext context)
  {
    List<PendingMessage> pending;
    lock (_messageLock)
    {
      pending = new List<PendingMessage>(_pendingMessages);
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
          context.Emit(new LogMessageCorrelatedEventArgs(snapshot, message.Message, delta));
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
  private void FlushStaleMessages(List<PendingMessage> pending)
  {
    if (_snapshotWindow.Count == 0 || pending.Count == 0)
      return;

    var oldestSnapshot = _snapshotWindow[0];
    var cutoffTime = oldestSnapshot.ClientTimestamp.AddSeconds(-MaxCorrelationDeltaSeconds);

    var staleMessages = pending.Where(msg =>
    {
      return !msg.Historical && msg.Timestamp < cutoffTime;
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
    PendingMessage message,
    out GameStateSnapshot bestSnapshot,
    out double bestDelta)
  {
    bestSnapshot = null!;
    bestDelta = double.MaxValue;

    // Native timestamps survive backfill; wrapper creation times do not.
    var msgClientTs = message.Timestamp;

    foreach (var snapshot in _snapshotWindow)
    {
      var delta = Math.Abs((snapshot.ClientTimestamp - msgClientTs).TotalSeconds);
      if (delta < bestDelta)
      {
        bestDelta = delta;
        bestSnapshot = snapshot;
      }
    }

    // On late attachment, earlier messages have no historical snapshots to
    // correlate with. Associate them with the first available state while
    // preserving their original message timestamps.
    return bestSnapshot != null && (bestDelta <= MaxCorrelationDeltaSeconds ||
      (message.Historical && msgClientTs < _snapshotWindow[0].ClientTimestamp));
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
