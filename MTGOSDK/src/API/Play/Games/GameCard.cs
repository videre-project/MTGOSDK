/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;
using static MTGOSDK.API.Events;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;
using GameCardEventArgs = MTGOSDK.API.Events.GameCardEventArgs;

public sealed class GameCard(dynamic gameCard) : DLRWrapper<IGameCard>
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj => Bind<IGameCard>(gameCard);

  /// <summary>
  /// Represents a game action performed by a card.
  /// </summary>
  public struct CardAction
  {
    /// <summary>
    /// The source card for the action.
    /// </summary>
    public GameCard Card { get; init; }

    /// <summary>
    /// The game action performed by the card.
    /// </summary>
    public GameAction Action { get; init; }

    public CardAction(dynamic cardAction)
    {
      Card = new(cardAction.Card);
      Action = new(cardAction.Action);
    }
  }

  /// <summary>
  /// Represents a card association (e.g. Target, Attacker, Effect source, etc.)
  /// </summary>
  public struct GameCardAssociation
  {
    /// <summary>
    /// Represents a card association (e.g. ChosenPlayer, TriggeringSource, etc.).
    /// </summary>
    /// <remarks>
    /// Requires the <c>WotC.MtGO.Client.Model.Play</c> reference assembly.
    /// </remarks>
    public CardAssociation Association { get; init; }

    /// <summary>
    /// The ID of the associated target.
    /// </summary>
    public int TargetId { get; init; }

    public GameCardAssociation(dynamic gameCardAssociation)
    {
      Association = Cast<CardAssociation>(Unbind(gameCardAssociation).Value);
      TargetId = gameCardAssociation.AssociatedTarget.Id;
    }
  }

  //
  // IGameCard wrapper properties
  //

  /// <summary>
  /// The unique identifier for this card instance.
  /// </summary>
  public int Id => Unbind(@base).Id;

  /// <summary>
  /// The source ID or 'thing' number of the card.
  /// </summary>
  /// <remarks>
  /// Retrieved from the <c>MagicProperty.SRC_THING_ID</c> property.
  /// </remarks>
  public int SourceId => @base.SourceId;

  public string Name => @base.Name;

  /// <summary>
  /// The associated card definition.
  /// </summary>
  public Card Definition => new(Unbind(@base).Definition);

  public int Timestamp => @base.Timestamp;

  public GameZone Zone => new(@base.Zone);

  public GameZone ActualZone => new(@base.ActualZone);

  public GameZone PreviousZone => new(@base.PreviousZone);

  public IEnumerable<CardAction> Actions =>
    Map<CardAction>(Unbind(@base).Actions);

  public IEnumerable<GameCard> PendingTargets =>
    Map<GameCard>(Unbind(@base).PendingTargets);

  public IEnumerable<GameCardAssociation> Associations =>
    Map<GameCardAssociation>(@base.Associations);

  // public IEnumerable<GameCard> AttackingOrders =>
  //   Map<GameCard>(Unbind(@base).AttackingOrders,
  //     new Func<dynamic, GameCard>((item) => new(item.Target)));

	// public IEnumerable<GameCard> BlockingOrders =>
  //   Map<GameCard>(Unbind(@base).BlockingOrders,
  //     new Func<dynamic, GameCard>((item) => new(item.Target)));

  /// <summary>
  /// The card's current counters.
  /// </summary>
  /// <remarks>
  /// Requires the <c>WotC.MtGO.Client.Model.Play</c> reference assembly.
  /// </remarks>
  public IEnumerable<Counter> Counters =>
    Map<Counter>(Unbind(@base).Counters,
      new Func<dynamic, Counter>((item) => Cast<Counter>(item)));

  public GamePlayer Owner => new(@base.Owner);

  // TODO: Derive this from the 'DuelSceneCardViewModel' instance.
  // public GamePlayer Controller => ???

  public GamePlayer Protector => new(@base.Protector);

  public GameCard OtherFace => new(@base.OtherFace);

  public int Power => @base.Power;

  public int Toughness => @base.Toughness;

  public int Loyalty => @base.Loyalty;

  public int Damage => @base.Damage;

  public int CurrentLevel => @base.CurrentLevel;

  public int CurrentDungeonRoom => @base.CurrentDungeonRoom;

  public int CurrentChapter => @base.CurrentChapter;

  public bool HasSummoningSickness => @base.HasSummoningSickness;

  public bool IsNewlyControlled => @base.IsNewlyControlled;

  public bool IsAttacking => @base.IsAttacking;

  public bool IsBlocking => @base.IsBlocking;

  public bool IsBlocked => @base.IsBlocked;

  public bool IsTapped => @base.IsTapped;

  public bool IsFlipped => @base.IsFlipped;

  public bool IsCompanion => @base.IsCompanion;

  public bool IsEmblem => @base.IsEmblem;

  public bool IsActivatedAbility => @base.IsActivatedAbility;

  public bool IsTriggeredAbility => @base.IsTriggeredAbility;

  public bool IsDelayedTrigger => @base.IsDelayedTrigger;

  public bool IsReplacementEffect => @base.IsReplacementEffect;

  public bool IsYieldAbility => @base.IsYieldAbility;

  public bool HasAutoTargets => @base.HasAutoTargets;

  //
  // IGameCard wrapper events
  //

  public EventProxy<GameCard, GameCardEventArgs> IsAttackingChanged =
    new(/* IGameCard */ gameCard, name: "IsAttackingChanged");

  public EventProxy<GameCard, GameCardEventArgs> IsBlockingChanged =
    new(/* IGameCard */ gameCard, name: "IsBlockingChanged");

  public EventProxy<GameCard, GameCardEventArgs> IsTappedChanged =
    new(/* IGameCard */ gameCard, name: "IsTappedChanged");

  public EventProxy<GameCard, GameCardEventArgs> DamageChanged =
    new(/* IGameCard */ gameCard, name: "DamageChanged");

  public EventProxy<GameCard, GameCardEventArgs> PowerChanged =
    new(/* IGameCard */ gameCard, name: "PowerChanged");

  public EventProxy<GameCard, GameCardEventArgs> ToughnessChanged =
    new(/* IGameCard */ gameCard, name: "ToughnessChanged");

  public EventProxy<GameCard, GameCardEventArgs> ZoneChanged =
    new(/* IGameCard */ gameCard, name: "ZoneChanged");

  public EventProxy<GameCard, GameCardEventArgs> AbilitiesChanged =
    new(/* IGameCard */ gameCard, name: "AbilitiesChanged");

  public EventProxy<GameCard, GameCardEventArgs> TypesChanged =
    new(/* IGameCard */ gameCard, name: "TypesChanged");
}
