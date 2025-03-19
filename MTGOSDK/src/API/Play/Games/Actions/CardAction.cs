/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using MTGOSDK.Core.Logging;
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

  //
  // Internal target set management
  //

  private bool _useTargetEvents = false;

  private readonly IList<TargetSet> _targetSets = [];

  private IList<TargetSet> _unsetTargetSets =>
    Map<IList, TargetSet>(cardAction.Targets);

  private IList<Targetable> _totalTargets =>
    _targetSets.SelectMany(t => t.CurrentTargets).ToList();

  private IList<uint>? _setTargetIds = null;

  private void UpdateTargets(TargetSet targetSet)
  {
    // Update the target set if it already exists.
    for (int i = 0; i < this._targetSets.Count; i++)
    {
      if (ReferenceEquals(this._targetSets[i], targetSet))
      {
        this._targetSets[i].CurrentTargets.AddRange(targetSet.CurrentTargets);
        return;
      }
    }

    // Otherwise, if the target set is not found, add it to the list.
    this._targetSets.Add(targetSet);
  }

  private void ClearTargets(TargetSet targetSet)
  {
    // Clear the target set if it already exists.
    for (int i = 0; i < this._targetSets.Count; i++)
    {
      if (ReferenceEquals(this._targetSets[i], targetSet))
      {
        this._targetSets[i].CurrentTargets.Clear();
        return;
      }
    }
  }

  private bool TargetsMatch(IList<uint>? allTargets)
  {
    if (allTargets == null) return false;

    return _totalTargets.Count == allTargets.Count &&
      _totalTargets.All(t => allTargets.Contains((uint)t.Id));
  }

  //
  // ICardAction wrapper properties
  //

  /// <summary>
  /// The source card for the action.
  /// </summary>
  public GameCard Card => new(@base.Card);

  /// <summary>
  /// The card action's targets.
  /// </summary>
  public IList<TargetSet> Targets =>
    _useTargetEvents ? _targetSets : _unsetTargetSets;

  /// <summary>
  /// Whether the card action requires a valid target selection to cast.
  /// </summary>
  public bool RequiresTargets { get; private set; } =
    cardAction.Targets.Count > 0;

  /// <summary>
  /// Whether the card action's targets have been set.
  /// </summary>
  public bool IsTargetsSet =>
    _targetSets.Count == _unsetTargetSets.Count && TargetsMatch(_setTargetIds);

  /// <summary>
  /// Whether the card action is a mana ability.
  /// </summary>
  public bool IsManaAbility => @base.IsManaAbility;

  //
  // ICardAction wrapper methods
  //

  private Action<CardAction, TargetSet> filter = null;

  public void ClearTargetEvents()
  {
    TargetSetChanged -= this.filter;
    this.filter = null;
  }

  /// <summary>
  /// Updates the targets for the card action when the target set is changed.
  /// </summary>
  public void UseTargetEvents()
  {
    if (_useTargetEvents) return;
    _useTargetEvents = true;

    filter = new((action, targetSet) =>
    {
      if (!Try<bool>(() =>
            action.Card.Id == this.Card.Id ||
            action.Card.SourceId == this.Card.SourceId) ||
          action.Name != this.Name ||
          action.Timestamp > this.Timestamp)
      {
        // If the interaction timestamp has advanced past this action, dispose.
        if (action.Timestamp > this.Timestamp) Dispose();
        return;
      }
      RequiresTargets |= true;

      //
      // If new items are still being added or the current selection is less
      // than the minimum targets, wait until the target set is complete.
      //
      TargetSetChange delta = targetSet.Delta;
      if (delta.NewItems != null || delta.OldItems == null &&
          delta.OldItems.Count < targetSet.MinimumTargets)
      {
        if (delta.NewItems != null) this._setTargetIds = null;
        ClearTargets(targetSet);
        return;
      }
      this._setTargetIds ??= [];

      //
      // We do however have access to the last cached NewCardActionMessage data,
      // so we can use that to determine whether the current transaction matches
      // the previous one to check if we're still in the same target selection.
      //
      // Usually this means that a previously selected target was deselected.
      //
      int numTargets = (int)Unbind(action).Message.NTargets;
      if (numTargets == 0 || numTargets < this._totalTargets.Count)
      {
        ClearTargets(targetSet);
        return;
      }

      //
      // Otherwise, if they have the same count but have any differing targets,
      // We must clear the current target selection and wait for the next reset
      // event to signal that the target selection has been confirmed.
      //
      // This may happen when still selecting targets for multiple target sets.
      //
      IList<uint> allTargets = Map<IList, uint>(Unbind(action).Message.Targets);
      if (numTargets == this._totalTargets.Count &&
          !allTargets.All(t => _totalTargets.Any(x => (uint)x.Id == t)) ||
          !_totalTargets.All(t => allTargets.Contains((uint)t.Id)))
      {
        ClearTargets(targetSet);
        return;
      }
      this._setTargetIds = allTargets;

      // Update the targetSet and the targetable objects' association.
      targetSet.CurrentTargets = Map<IList, Targetable>(delta.OldItems);
      foreach (var t in targetSet.CurrentTargets) t.parentSet = targetSet;
      UpdateTargets(targetSet);

      // If we're done selecting targets, signal the event.
      if (this.IsTargetsSet) this.OnTargetsSet?.Invoke(this);
    });

    TargetSetChanged += filter;
    this.OnTargetsSet += (CardAction _) => ClearTargetEvents();
  }

  //
  // ICardAction wrapper events
  //

  public event Action<CardAction> OnTargetsSet;

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
