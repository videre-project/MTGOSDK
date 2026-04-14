/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Play.Games.Processors.EventArgs;


namespace MTGOSDK.API.Play.Games.Processors;


/// <summary>
/// Paired current/previous snapshots passed to <see cref="IGameStateProcessor"/>
/// instances after a new game state is built from a gameplay-status message.
/// </summary>
public sealed class GameContext
{
  /// <summary>
  /// The current game state snapshot.
  /// </summary>
  public required GameStateSnapshot Current { get; init; }

  /// <summary>
  /// The previous game state snapshot.
  /// </summary>
  public required GameStateSnapshot Previous { get; init; }

  /// <summary>
  /// Maps current ThingIDs to their previous ThingIDs for cards that changed ID.
  /// </summary>
  public Dictionary<int, int> CardAncestryMap { get; } = new();

  /// <summary>
  /// Damage assignments extracted from DamageAssignment state elements.
  /// Empty when no assignments are present in the current message.
  /// </summary>
  public List<CombatDamageAssignmentAction> DamageAssignments { get; internal set; } = new();

  /// <summary>
  /// ThingIDs of SUBC cards (adventure halves, split faces, back faces).
  /// Populated by <see cref="GameProcessor"/> during snapshot construction
  /// so downstream processors can filter them out.
  /// </summary>
  public HashSet<int> SubCardIds { get; internal set; } = new();

  /// <summary>
  /// Cards revealed via the <c>HandleRevealedCard</c> (opcode 4635) pathway —
  /// opponent-visible reveals such as Gitaxian Probe or hand-reveal effects.
  /// Non-empty only when the <c>s_handleRevealedCard</c> hook fires and
  /// <see cref="GameProcessor.BuildRevealedZoneCards"/> returns synthetic cards.
  /// <para>
  /// Scry / top-of-library effects use ThingElements (Zone = 0xFFFFFF) and
  /// appear in <see cref="GameStateSnapshot.HiddenCards"/> instead.
  /// <see cref="RevealedZoneTracker"/> merges both sources.
  /// </para>
  /// </summary>
  public List<GameCard> RevealedZoneCards { get; internal set; } = new();

  /// <summary>
  /// Reference to the host processor for centralized event dispatch.
  /// </summary>
  internal GameProcessor? Processor { get; init; }

  /// <summary>
  /// Emits an event through the centralized event bus on the host
  /// <see cref="GameProcessor"/>. The <see cref="GameEventArgs.Context"/>
  /// and <see cref="GameEventArgs.Nonce"/> are stamped automatically.
  /// </summary>
  public void Emit<T>(T args) where T : GameEventArgs =>
    Processor?.Dispatch(args, this);
}
