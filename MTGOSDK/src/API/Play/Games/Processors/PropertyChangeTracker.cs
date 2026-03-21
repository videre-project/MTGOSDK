/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Tracks property changes for cards and players between snapshots.
/// Uses the GameContext.CardAncestryMap to correlate card identities across ID changes.
/// </summary>
public sealed class PropertyChangeTracker : IGameStateProcessor
{
  public void Process(GameContext context)
  {
    // Diff cards
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

      if (prev != null)
      {
        var oldPartial = GetPartial(prev);
        var newPartial = GetPartial(curr);
        if (oldPartial == null || newPartial == null) continue;

        if (!oldPartial.Equals(newPartial))
        {
          context.Emit(new CardChangedEventArgs(prev, curr));
        }
      }
    }

    // Diff players
    foreach (var (idx, currPlayer) in context.Current.Players)
    {
      if (context.Previous.Players.TryGetValue(idx, out var prevPlayer))
      {
        var oldPartial = GetPlayerPartial(prevPlayer);
        var newPartial = GetPlayerPartial(currPlayer);
        if (oldPartial == null || newPartial == null) continue;

        if (!oldPartial.Equals(newPartial))
        {
          context.Emit(new PlayerChangedEventArgs(idx, prevPlayer, currPlayer));
        }
      }
      else
      {
        // New player appeared
        context.Emit(new PlayerChangedEventArgs(idx, currPlayer, currPlayer));
      }
    }
  }

  private static GameCardPartial? GetPartial(GameCard card)
  {
    try
    {
      if (card is DLRWrapper wrapper)
      {
        var obj = DLRWrapper.Unbind(wrapper);
        if (obj is GameCardPartial gcp)
        {
          return gcp;
        }
      }
    }
    catch (Exception)
    {
      // Swallow errors from unbound cards
    }
    return null;
  }

  private static GamePlayerPartial? GetPlayerPartial(GamePlayer player)
  {
    try
    {
      if (player is DLRWrapper wrapper)
      {
        var obj = DLRWrapper.Unbind(wrapper);
        if (obj is GamePlayerPartial gpp)
        {
          return gpp;
        }
      }
    }
    catch (Exception)
    {
      // Swallow errors from unbound players
    }
    return null;
  }
}
