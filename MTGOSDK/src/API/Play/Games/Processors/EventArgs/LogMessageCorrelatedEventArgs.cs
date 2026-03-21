/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Chat;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event args for a correlated log message and snapshot pair.
/// </summary>
public sealed class LogMessageCorrelatedEventArgs(
  GameStateSnapshot snapshot,
  Message message,
  double timeDeltaSeconds) : GameEventArgs
{
  /// <summary>
  /// The game state snapshot that the log message correlates with.
  /// </summary>
  public new GameStateSnapshot Snapshot { get; } = snapshot;

  /// <summary>
  /// The log message that was correlated.
  /// </summary>
  public Message Message { get; } = message;

  /// <summary>
  /// The time delta (in seconds) between the message and snapshot client timestamps.
  /// Lower values indicate higher confidence in the correlation.
  /// </summary>
  public double TimeDeltaSeconds { get; } = timeDeltaSeconds;

  /// <summary>
  /// The message's server timestamp.
  /// </summary>
  public DateTime MessageServerTimestamp => Message.Timestamp;

  /// <summary>
  /// The message's client receive timestamp (from <c>Unbind(message).__timestamp</c>).
  /// </summary>
  public DateTime MessageClientTimestamp => Unbind(Message).__timestamp;

  /// <summary>
  /// The snapshot's server ActionTimestamp.
  /// </summary>
  public DateTime SnapshotServerTimestamp => Snapshot.ActionTimestamp;

  /// <summary>
  /// The snapshot's client receive timestamp (from <c>instance.__timestamp</c>).
  /// </summary>
  public DateTime SnapshotClientTimestamp => Snapshot.ClientTimestamp;
}
