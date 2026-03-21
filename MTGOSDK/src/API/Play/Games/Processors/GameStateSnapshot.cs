/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// A single tick's worth of game state: timing, phase, and entity snapshots.
/// </summary>
public sealed class GameStateSnapshot
{
  /// <summary>
  /// Server-assigned timestamp for this game state.
  /// </summary>
  public required uint Timestamp { get; init; }

  /// <summary>
  /// When this state representation was logged by the server.
  /// </summary>
  public required DateTime ActionTimestamp { get; init; }

  /// <summary>
  /// Client-side receive time for this game state update (from <c>instance.__timestamp</c>).
  /// Computed the same way as <c>Unbind(message).__timestamp</c> on log messages.
  /// </summary>
  public required DateTime ClientTimestamp { get; init; }

  /// <summary>
  /// The interaction timestamp from the state element (matches <see cref="GamePrompt.Timestamp"/>).
  /// </summary>
  public required uint InteractionTimestamp { get; init; }

  /// <summary>
  /// The current turn number (from TurnStepElement or InteractStateElement).
  /// </summary>
  public required int TurnNumber { get; init; }

  /// <summary>
  /// The current game phase (from TurnStepElement or InteractStateElement).
  /// </summary>
  public required GamePhase CurrentPhase { get; init; }

  /// <summary>
  /// The type of state element that produced this snapshot (TurnStep or InteractState).
  /// </summary>
  public required StateElementType StateType { get; init; }

  /// <summary>
  /// The player index that the current prompt targets (byte.MaxValue = all players).
  /// </summary>
  public required byte PromptedPlayer { get; init; }

  /// <summary>
  /// The current prompt text from the state element.
  /// </summary>
  public required string PromptText { get; init; }

  /// <summary>
  /// A deterministic nonce derived from the prompt state, used to correlate
  /// this snapshot with the corresponding <see cref="GamePrompt"/> event.
  /// </summary>
  public int Nonce => ComputeNonce(InteractionTimestamp, PromptedPlayer, PromptText);

  /// <summary>
  /// Computes a deterministic hash from prompt state fields.
  /// </summary>
  public static int ComputeNonce(uint timestamp, byte promptedPlayer, string promptText)
  {
    var hash = new HashCode();
    hash.Add(timestamp);
    hash.Add(promptedPlayer);
    hash.Add(promptText ?? string.Empty, StringComparer.Ordinal);
    return hash.ToHashCode();
  }

  /// <summary>
  /// Visible cards keyed by ThingID.
  /// </summary>
  public required Dictionary<int, GameCard> Cards { get; init; }

  /// <summary>
  /// Transient hidden cards keyed by ThingID.
  /// </summary>
  public required Dictionary<int, GameCard> HiddenCards { get; init; }

  /// <summary>
  /// Player snapshots keyed by player index.
  /// </summary>
  public required Dictionary<int, GamePlayer> Players { get; init; }
}
