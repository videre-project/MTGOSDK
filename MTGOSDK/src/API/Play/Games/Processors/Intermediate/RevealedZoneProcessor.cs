/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors.Intermediate;

/// <summary>
/// Emits arrivals and departures for captured reveals and exposed library cards.
/// </summary>
/// <remarks>
/// Native revealed-zone objects have logical addresses even when their server IDs
/// are unassigned. Comparing those addresses preserves distinct copies of a card.
/// Departures follow captured zone membership and retain the last captured values.
/// </remarks>
public sealed class RevealedZoneProcessor : IIntermediateTickProcessor
{
  private Dictionary<ulong, GameCard> _activeReveals = new();

  public void Process(GameContext context)
  {
    var current = new Dictionary<ulong, GameCard>(context.Current.RevealedCards);

    // Library and pile reveals still arrive as ThingElements. Exclude unused
    // faces, and merge by native identity when a card also occurs in a reveal zone.
    foreach (var card in context.Current.HiddenCards.Values.Where(card =>
      card.Id > 0 && !context.SubCardIds.Contains(card.Id)
      && (card.Zone?.Name is "Revealed" or "Pile1" or "Pile2" or "Pile3"
          || (card.Zone?.Name == "Library" && card.SourceId == -1))))
    {
      ulong address = ((GameCardPartial)Unbind(card)).LogicalAddress
        ?? throw new InvalidOperationException("Hidden reveal capture has no logical address.");
      current.TryAdd(address, card);
    }

    var arrived = current.Where(pair => !_activeReveals.ContainsKey(pair.Key))
      .Select(pair => pair.Value).ToList();
    var departed = _activeReveals.Where(pair => !current.ContainsKey(pair.Key))
      .Select(pair => pair.Value).ToList();

    // Retain this tick's detached values even when membership is unchanged.
    _activeReveals = current;
    if (arrived.Count == 0 && departed.Count == 0)
      return;

    context.Emit(new RevealedCardsEventArgs
    {
      Arrived  = arrived,
      Departed = departed,
      Current  = current.Values.ToList(),
    });
  }
}
