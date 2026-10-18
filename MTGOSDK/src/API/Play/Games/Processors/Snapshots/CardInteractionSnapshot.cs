/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Remoting.Batching;


namespace MTGOSDK.API.Play.Games.Processors.Snapshots;

/// <summary>
/// A card's native actions and selection flags from one scene capture.
/// </summary>
/// <remarks>
/// Retained game updates keep these values even after the UI advances.
/// Action card/player references in a game update resolve against its server snapshot.
/// </remarks>
public sealed class CardInteractionSnapshot : IEquatable<CardInteractionSnapshot>
{
  private readonly RemoteBatchObject _captured;
  private readonly GameCardPartial? _resolvedCard;

  /// <summary>
  /// The native card object's Diver logical address, independent of MTGO IDs.
  /// </summary>
  public ulong LogicalAddress => _captured.Get<RemoteBatchObject>("card").LogicalAddress
    ?? throw new InvalidOperationException("Captured card has no logical address.");

  /// <summary>
  /// The native card ID in this scene capture.
  /// </summary>
  public int CardId => Selection.CardId;

  /// <summary>
  /// The native source ID captured with the card.
  /// </summary>
  public int SourceId { get; }

  /// <summary>
  /// The card name captured with its native identity.
  /// </summary>
  public string Name => _captured.Get<RemoteBatchObject>("card").Get<string>("Name");

  /// <summary>
  /// The scene zone containing this card, including pending trigger zones.
  /// </summary>
  public CardZone Zone => _captured.Get<CardZone>("zone");

  /// <summary>
  /// The catalog ID of the card definition, when one is available.
  /// </summary>
  public int? CatalogId => _captured.Get<int?>("catalogId");

  /// <summary>
  /// The displayed rules text for the card or ability at capture time.
  /// </summary>
  public string RulesText => MtgoTextNormalizer.NormalizeText(_captured.Get<string>("rulesText"));

  /// <summary>
  /// The card's action and selection eligibility at capture time.
  /// </summary>
  public CardSelectable Selection { get; }

  /// <summary>
  /// The complete native action list at capture time.
  /// </summary>
  public IReadOnlyList<CardAction> Actions { get; }

  // Only correlated publications bind the owner to captured gameplay state.
  internal GameCard? Card =>
    field ??= _resolvedCard is null
      ? null
      : new GameCard(_resolvedCard);

  internal CardInteractionSnapshot(RemoteBatchObject captured,
    Func<object?, object?>? resolve = null)
  {
    _captured = captured;
    _resolvedCard = resolve?.Invoke(captured.Get<RemoteBatchObject>("card")) as GameCardPartial;
    SourceId = captured.Get<int>("sourceId");
    Selection = new(
      captured.Get<int>("id"),
      captured.Get<bool>("hasActions"),
      captured.Get<bool>("selectable"),
      captured.Get<bool>("targetable"),
      captured.Get<bool>("targeted"))
    {
      IsActive = captured.Get<bool>("active"),
      IsBeingCast = captured.Get<bool>("beingCast")
    };
    Actions = Array.AsReadOnly(captured.GetSequence<RemoteBatchObject>("actions")
      .Select(action => (CardAction)GameAction.MaterializeCaptured(
        resolve is null ? action : (RemoteBatchObject)resolve(action)!)).ToArray());
  }

  internal CardInteractionSnapshot Resolve(Func<object?, object?> resolve) =>
    new(_captured, resolve);

  internal bool HasSameActions(CardInteractionSnapshot other) =>
    CapturedValueComparer.Instance.Equals(_captured.Members["actions"], other._captured.Members["actions"]);

  // Display flags can change before the prompt or available interaction changes.
  internal bool HasSameInteraction(CardInteractionSnapshot other) =>
    LogicalAddress == other.LogicalAddress
    && _captured.Members.Count == other._captured.Members.Count
    && _captured.Members.All(pair => pair.Key is "active" or "beingCast"
      || other._captured.Members.TryGetValue(pair.Key, out var value)
        && CapturedValueComparer.Instance.Equals(pair.Value, value));

  public bool Equals(CardInteractionSnapshot? other) =>
    other is not null && _captured.Equals(other._captured);

  public override bool Equals(object? other) =>
    other is CardInteractionSnapshot card && Equals(card);

  public override int GetHashCode() => _captured.GetHashCode();
}
