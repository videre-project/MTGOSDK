/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Serialization;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a game action performed by a card.
/// </summary>
public class CardAction(dynamic cardAction) : GameAction, IDisposable
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj => Bind<ICardAction>(cardAction);

  internal record class TargetSetChange(dynamic NewItems, dynamic OldItems);

  public void Dispose()
  {
    ClearTargetEvents();
    this.OnTargetsSet = null;
  }

  // Internal target set management
  private bool _useTargetEvents = false;

  private IList<TargetSet>? _targetSets;

  private IList<TargetSet> GetTargetSets()
  {
    if (_targetSets != null) return _targetSets;

    // Some remote CardAction instances throw during early target set materialization
    // (for example while targets are still mutating). Do not fail action wrapping.
    _targetSets = Try<IList<TargetSet>>(() => Map<IList, TargetSet>(cardAction.Targets))
      ?? new List<TargetSet>();
    return _targetSets;
  }

  private void UpdateTargets(TargetSet targetSet)
  {
    var targetSets = GetTargetSets();

    // Wrapper reference identity is unstable across event snapshots; use index.
    int index = targetSet.Index;
    if (index < 0) return;

    for (int i = 0; i < targetSets.Count; i++)
    {
      if (targetSets[i].Index == index)
      {
        targetSets[i].CurrentTargets = targetSet.CurrentTargets;
        return;
      }
    }

    targetSets.Add(targetSet);
  }

  private void ClearTargets(TargetSet targetSet)
  {
    var targetSets = GetTargetSets();

    int index = targetSet.Index;
    if (index < 0) return;

    for (int i = 0; i < targetSets.Count; i++)
    {
      if (targetSets[i].Index == index)
      {
        targetSets[i].CurrentTargets.Clear();
        return;
      }
    }
  }

  /// <summary>
  /// The raw NewCardActionMessage from the server, used for target reconciliation.
  /// Set by GameActionPerformed when the action is created.
  /// </summary>
  internal dynamic? Message { get; set; }

  //
  // ICardAction wrapper properties
  //

  /// <summary>
  /// The source card for the action.
  /// </summary>
  public GameCard Card => new(@base.Card);

  /// <summary>
  /// The collection of target sets for this card action.
  /// </summary>
  public IList<TargetSet> Targets => GetTargetSets();

  /// <summary>
  /// Whether the card action requires a valid target selection to cast.
  /// </summary>
  public bool RequiresTargets => Targets.Count > 0;

  /// <summary>
  /// Whether the card action's targets have been set.
  /// </summary>
  public bool IsTargetsSet => Targets.All(t => t.IsSet);

  /// <summary>
  /// Whether the card action is a mana ability.
  /// </summary>
  public bool IsManaAbility => @base.IsManaAbility;

  //
  // ICardAction wrapper events
  //

  public event Action<CardAction>? OnTargetsSet;

  internal void TriggerTargetsSet() => OnTargetsSet?.Invoke(this);

  //
  // ICardAction wrapper methods
  //

  private Action<CardAction, TargetSet>? filter = null;

  public void ClearTargetEvents()
  {
    if (this.filter == null) return;
    TargetSetChanged -= this.filter;
    this.filter = null;
  }

  /// <summary>
  /// Updates the targets for the card action when target set changes are observed.
  /// </summary>
  public void UseTargetEvents()
  {
    if (_useTargetEvents) return;
    _useTargetEvents = true;

    // Initialize snapshot once (best effort), then let event deltas refine it.
    _ = GetTargetSets();

    filter = new((action, targetSet) =>
    {
      if (!Try<bool>(() =>
            action.Card.Id == this.Card.Id ||
            action.Card.SourceId == this.Card.SourceId) ||
          action.Name != this.Name ||
          action.Timestamp > this.Timestamp)
      {
        if (action.Timestamp > this.Timestamp) Dispose();
        return;
      }

      TargetSetChange? delta = targetSet.Delta;
      if (delta == null) return;

      if (delta.NewItems != null ||
          delta.OldItems == null ||
          delta.OldItems.Count < targetSet.MinimumTargets)
      {
        ClearTargets(targetSet);
        return;
      }

      targetSet.CurrentTargets = Map<IList, Targetable>(delta.OldItems);
      foreach (var t in targetSet.CurrentTargets)
      {
        t.parentSet = targetSet;
      }

      UpdateTargets(targetSet);

      if (this.IsTargetsSet)
      {
        this.OnTargetsSet?.Invoke(this);
      }
    });

    TargetSetChanged += filter;
    this.OnTargetsSet += (CardAction _) => ClearTargetEvents();
  }

  //
  // IGame static events
  //

  public static EventHookProxy<CardAction, TargetSet> TargetSetChanged =
    new(
      "WotC.MtGO.Client.Model.Play.Actions.TargetSet",
      "CurrentTargets_CollectionChanged",
      new((instance, args) =>
      {
        //
        // Since we cannot recover the entire state of the collection each time
        // a new item is added, we instead wait until the collection is cleared
        // when resetting the targets.
        //
        var e = args[1];
        TargetSet targetSet = new(instance)
        {
          Delta = new TargetSetChange(e.NewItems, e.OldItems),
        };

        //
        // Since this event is triggered after the targets are set, we need
        // to decrement the timestamp by one to match the original timestamp.
        //
        // No other timestamp will overlap with the current timestamp since it
        // is always incremented after a target is set and then again once a new
        // game action is performed. Hence, actions always differ by at least 2.
        //
        CardAction action = targetSet.Action;
        action.SetTimestamp(action.Timestamp - 1);

        return (action, targetSet);
      })
    );
}
