/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.Core.Logging;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Tracks zone changes between snapshots: departures, arrivals, same-ID moves,
/// and server-chain / texture-match correlations.
/// </summary>
public sealed class ZoneChangeTracker : IIntermediateTickProcessor
{
  /// <summary>
  /// Accumulated old-ThingID → new-ThingID chain for the lifetime of the game.
  /// </summary>
  private readonly Dictionary<int, int> _idChain = new();

  public void Process(GameContext context)
  {
    var current  = context.Current.Cards;
    var previous = context.Previous.Cards;

    // Diff against previous snapshot to find raw departures,
    // arrivals, and same-ID moves.
    var departed = new List<GameCard>();
    var arrived  = new List<GameCard>();
    var moved    = new List<(GameCard From, GameCard To)>();

    foreach (var (tid, prev) in previous)
    {
      if (!current.ContainsKey(tid))
        departed.Add(prev);
    }

    foreach (var (tid, curr) in current)
    {
      if (!previous.TryGetValue(tid, out var prev))
        arrived.Add(curr);
      else if (prev.Zone != curr.Zone)
      {
        context.CardAncestryMap[tid] = tid;
        moved.Add((prev, curr));
      }
      else
      {
        context.CardAncestryMap[tid] = tid;
      }
    }

    var inferredHiddenDepartures = new Dictionary<int, GameCard>();
    foreach (var arr in arrived)
    {
      if (arr.SourceId == -1 || arr.SourceId == arr.Id) continue;
      // Abilities are created fresh on the stack — their SourceId points to
      // the source permanent, not a zone-chain predecessor. Don't let them
      // trigger hidden-departure inference.
      if (arr.IsTriggeredAbility || arr.IsActivatedAbility) continue;

      if (context.Current.HiddenCards.TryGetValue(arr.SourceId, out var hiddenCard))
        inferredHiddenDepartures[arr.SourceId] = hiddenCard;
    }

    // Correlate departures → arrivals by server chain and texture match
    var unmatchedDeps = new List<GameCard>(departed);
    var unmatchedArrs = new List<GameCard>();
    var chainLogs     = new List<string>();

    foreach (var arr in arrived)
    {
      // Abilities are created fresh on the stack — they don't move from any
      // zone. Their SourceId may reference the source permanent's ThingID,
      // which can collide with a real zone-change departure (e.g. an ETB
      // trigger's SourceId matches the hand card that just moved to the
      // battlefield). Skip correlation so abilities don't steal departure
      // records from cards that genuinely changed zones.
      if (arr.IsTriggeredAbility || arr.IsActivatedAbility)
      {
        unmatchedArrs.Add(arr);
        continue;
      }

      bool chained = false;
      if (arr.SourceId != -1 && arr.SourceId != arr.Id)
      {
        var dep = unmatchedDeps.FirstOrDefault(d => d.Id == arr.SourceId);
        inferredHiddenDepartures.TryGetValue(arr.SourceId, out var hid);

        // Match against cards that were hidden in the previous snapshot
        context.Previous.HiddenCards.TryGetValue(arr.SourceId, out var prevHid);

        if (dep != null || hid != null || prevHid != null)
        {
          context.Current.HiddenCards.Remove(arr.SourceId);
          _idChain[arr.SourceId] = arr.Id;
          context.CardAncestryMap[arr.Id] = arr.SourceId;
          if (dep != null) unmatchedDeps.Remove(dep);

          // When chaining exclusively via prevHid (adventure/split SUBC arriving
          // on Stack), find the parent card in unmatchedDeps via SplitChildId and
          // use it as fromCard so the event reads "Spell (Moved) Hand → Stack".
          // The parent departure is suppressed (consumed into the Moved pair).
          GameCard? subcParent = null;
          if (prevHid != null && dep == null && hid == null)
          {
            int subcId = arr.SourceId;
            subcParent = unmatchedDeps.FirstOrDefault(
              d => d.SplitChildId0 == subcId || d.SplitChildId1 == subcId);
            if (subcParent != null)
              unmatchedDeps.Remove(subcParent);
          }

          string fromZone = dep != null ? ZoneName(dep.Zone)
                          : subcParent != null ? ZoneName(subcParent.Zone)
                          : hid != null ? ZoneName(hid.Zone)
                          : prevHid != null ? ZoneName(prevHid.Zone)
                          : arr.PreviousZone != null ? ZoneName(arr.PreviousZone)
                          : "Unknown";

          string symbol = IsHiddenZone(fromZone) ? "[+]" : "[=>]";
          chainLogs.Add(
            $"  {symbol} {arr.Name}: {arr.SourceId} => {arr.Id}" +
            $" ({fromZone} -> {ZoneName(arr.Zone)}) (server chain)");

          var fromCard = dep ?? subcParent ?? hid ?? prevHid!;
          moved.Add((fromCard, arr));
          chained = true;
        }
      }

      if (!chained)
      {
        unmatchedArrs.Add(arr);
      }
    }

    // Collect hidden cards that were present in the last snapshot but aren't
    // in the current hidden pool — these are our "hidden departures".
    var hiddenDepartures = context.Previous.HiddenCards.Values
      .Where(d => !context.Current.HiddenCards.ContainsKey(d.Id))
      .ToList();

    // Exclude abilities from texture-match candidates — their CTN is
    // inherited from the source permanent and would falsely match against
    // zone-changing copies of that permanent (e.g. a Brokers Hideout trigger
    // on the Stack texture-matching the land entering the Graveyard).
    var candidates = unmatchedDeps
      .Where(d => !d.IsTriggeredAbility && !d.IsActivatedAbility)
      .Concat(hiddenDepartures).ToList();
    if (candidates.Count > 0 && unmatchedArrs.Count > 0)
    {
      var byTexture = candidates
        .Where(d => d.CTN > 0)
        .GroupBy(d => d.CTN)
        .ToDictionary(g => g.Key, g => new Queue<GameCard>(g));

      foreach (var arr in unmatchedArrs.ToList())
      {
        // Abilities should never texture-match against departures either
        if (arr.IsTriggeredAbility || arr.IsActivatedAbility) continue;

        if (arr.CTN > 0
            && byTexture.TryGetValue(arr.CTN, out var q)
            && q.Count > 0)
        {
          //
          // Only reject if the arrival's explicitly-reported PreviousZone
          // contradicts where we saw it depart from. If MTGO failed to populate
          // PreviousZone (it's null, Nowhere, or even its destination zone),
          // we still accept the texture match.
          //
          // This prevents mulligan mismatches without breaking Normal Play.
          //
          var dep = q.Peek();
          bool isContradictory = arr.PreviousZone != null
                              && arr.PreviousZone != CardZone.Nowhere
                              && arr.PreviousZone != arr.Zone
                              && dep.Zone != null
                              && arr.PreviousZone != dep.Zone;

          if (isContradictory) continue;

          q.Dequeue();
          _idChain[dep.Id] = arr.Id;
          context.CardAncestryMap[arr.Id] = dep.Id;
          unmatchedArrs.Remove(arr);
          unmatchedDeps.Remove(dep);

          // Promote to a Moved pair so it renders with from/to zones
          moved.Add((dep, arr));

          string fromZone = ZoneName(dep.Zone);
          string symbol = IsHiddenZone(fromZone) ? "[+]" : "[=>]";
          chainLogs.Add(
            $"  {symbol} {dep.Name}: {dep.Id} => {arr.Id}" +
            $" ({fromZone} -> {ZoneName(arr.Zone)}) (texture match)");
        }
      }
    }

    var unresolvedOrigins = new List<string>();
    foreach (var arr in unmatchedArrs.Where(
               a => _idChain.ContainsValue(a.Id)
                 || _idChain.ContainsKey(a.Id)))
    {
      int origin = ResolveOrigin(_idChain, arr.Id);
      if (origin != arr.Id)
        unresolvedOrigins.Add(
          $"  [*] {arr.Name} (ID:{arr.Id})" +
          $" traces back to origin ID:{origin}");
    }

    // Fire event through centralized bus
    int totalDepartures = departed.Count + inferredHiddenDepartures.Count;
    if (arrived.Count > 0 || totalDepartures > 0 || moved.Count > 0)
    {
      context.Emit(new ZoneChangeEventArgs
      {
        Arrived = unmatchedArrs,
        Departed = unmatchedDeps,
        Moved = moved,
        ChainLogs = chainLogs,
        UnresolvedOrigins = unresolvedOrigins
      });
    }
  }

  /// <summary>
  /// Resolve a zone to a human-readable name.
  /// </summary>
  private static string ZoneName(GameZone? zone) =>
    zone?.Name ?? zone?.Zone.ToString() ?? "Unknown";

  private static bool IsHiddenZone(string zone) =>
    zone == "Library" || zone == "Sideboard" || zone == "Nowhere" || zone == "Unknown";

  /// <summary>
  /// Walk the IdChain backwards to find the original ThingID.
  /// </summary>
  private static int ResolveOrigin(Dictionary<int, int> chain, int thingId)
  {
    // Build reverse lookup: newId → oldId
    var reverse = chain.ToDictionary(kv => kv.Value, kv => kv.Key);
    int current = thingId;
    while (reverse.TryGetValue(current, out int prev))
    {
      current = prev;
    }

    return current;
  }
}
