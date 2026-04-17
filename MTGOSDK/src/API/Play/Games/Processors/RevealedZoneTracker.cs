/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.EventArgs;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Tracks cards that enter or leave a Revealed or Pile zone and emits
/// <see cref="RevealedCardsEventArgs"/> when any change is detected.
/// <para>
/// Scry, tutor, and other "look at top of library" effects send the
/// temporarily-visible cards via ThingElements with <c>SourceId = -1</c>
/// and their actual zone (typically <c>Library</c>). The
/// <c>IsTransientHiddenCard</c> filter in <see cref="GameProcessor"/> catches
/// these and places them in <c>_HiddenCards</c>, so they appear in
/// <see cref="GameStateSnapshot.HiddenCards"/> of each snapshot.
/// </para>
/// <para>
/// Arrivals and departures are detected by diffing the filtered HiddenCards
/// between the current and previous snapshots — no separate hook or
/// departure message is needed.
/// </para>
/// </summary>
public sealed class RevealedZoneTracker : IIntermediateTickProcessor
{
  /// <summary>
  /// Cards that were in the revealed zone on the previous tick.
  /// Cleared after departures so that cards stuck in _HiddenCards
  /// (with unchanging SourceId == -1) are re-detected as arrivals
  /// on subsequent reveal activations.
  /// </summary>
  private HashSet<int> _activeRevealIds = new();

  public void Initialize(Game game) { }

  public void Process(GameContext context)
  {
    // Filter out:
    //   - Negative-ID "reveal markers" (opcode 4635) that mark the chosen card
    //   - SUBC cards (adventure/split halves) to avoid double-counting
    var subCards = context.SubCardIds;
    var current = context.Current.HiddenCards.Values
      .Where(c => c.Id > 0 && !subCards.Contains(c.Id)
                  && (c.Zone?.Name is "Revealed" or "Pile1" or "Pile2" or "Pile3"
                      || (c.Zone?.Name == "Library" && c.SourceId == -1)))
      .Concat(context.RevealedZoneCards.Where(c => c.Id > 0 && !subCards.Contains(c.Id)))
      .ToList();

    var currIds = current.Select(c => c.Id).ToHashSet();

    var arrived  = current.Where(c => !_activeRevealIds.Contains(c.Id)).ToList();
    var departed = _activeRevealIds.Where(id => !currIds.Contains(id)).ToList();

    if (arrived.Count == 0 && departed.Count == 0) return;

    // Resolve departed GameCard objects from previous snapshot
    var departedCards = departed
      .Select(id => context.Previous.HiddenCards.TryGetValue(id, out var c) ? c : null)
      .Where(c => c != null)
      .ToList()!;

    // Update active set: add arrivals, remove departures
    foreach (var c in arrived) _activeRevealIds.Add(c.Id);
    foreach (var id in departed) _activeRevealIds.Remove(id);

    context.Emit(new RevealedCardsEventArgs
    {
      Arrived  = arrived,
      Departed = departedCards!,
      Current  = current,
    });
  }
}
