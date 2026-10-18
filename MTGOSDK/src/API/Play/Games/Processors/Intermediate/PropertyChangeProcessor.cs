/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Reflection;


namespace MTGOSDK.API.Play.Games.Processors.Intermediate;

/// <summary>
/// Tracks property changes for cards and players between snapshots.
/// Uses the GameContext.CardAncestryMap to correlate card identities across ID changes.
/// </summary>
public sealed class PropertyChangeProcessor : IIntermediateTickProcessor
{
  public void Process(GameContext context)
  {
    // Compare card state after resolving any ThingID changes.
    foreach (var (tid, curr) in context.Current.Cards)
    {
      GameCard? prev = null;
      if (context.CardAncestryMap.TryGetValue(tid, out int prevId))
      {
        if (context.Previous.Cards.TryGetValue(prevId, out var prevCard))
          prev = prevCard;
        else if (context.Previous.HiddenCards.TryGetValue(prevId, out var hiddenCard))
          prev = hiddenCard;
      }
      else if (context.Previous.Cards.TryGetValue(tid, out var sameIdCard))
      {
        prev = sameIdCard;
      }

      if (prev is null)
        continue;
      if ((object)DLRWrapper.Unbind(prev) is not GameCardPartial oldPartial ||
          (object)DLRWrapper.Unbind(curr) is not GameCardPartial newPartial)
        continue;

      if (!oldPartial.Equals(newPartial))
        context.Emit(new CardChangedEventArgs(prev, curr));
    }

    // Compare players by server-order index.
    foreach (var (idx, currPlayer) in context.Current.Players)
    {
      if (context.Previous.Players.TryGetValue(idx, out var prevPlayer))
      {
        if ((object)DLRWrapper.Unbind(prevPlayer) is not GamePlayerPartial oldPartial ||
            (object)DLRWrapper.Unbind(currPlayer) is not GamePlayerPartial newPartial)
          continue;

        if (!oldPartial.Equals(newPartial))
        {
          context.Emit(new PlayerChangedEventArgs(idx, prevPlayer, currPlayer));
        }
      }
      else
      {
        // A newly observed player has no prior state to compare.
        context.Emit(new PlayerChangedEventArgs(idx, currPlayer, currPlayer));
      }
    }
  }
}
