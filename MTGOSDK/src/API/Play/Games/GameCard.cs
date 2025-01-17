/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;
using static MTGOSDK.API.Events;

public sealed class GameCard(dynamic gameCard) : DLRWrapper<IGameCard>
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj => Bind<IGameCard>(gameCard);

  private struct OrderedCombatParticipant(dynamic orderedCombatParticipant)
  {
    public int Order = orderedCombatParticipant.Order;
    public GameCard Target = new(orderedCombatParticipant.Target);
  }

  // DigitalMagicObject -> CardRelationshipData -> DuelSceneCardViewModel
  private readonly dynamic cardModel = gameCard.Tag.CardViewModel;

  public bool IsDisposed => gameCard.Tag == null;

  //
  // IGameCard wrapper properties
  //

  /// <summary>
  /// The unique identifier for this card instance.
  /// </summary>
  public int Id => Unbind(@base).Id;

  /// <summary>
  /// The unique card texture number for the card object.
  /// </summary>
  public int CTN => Unbind(@base).CTN;

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

  /// <summary>
  /// The latest interaction timestamp associated with the card.
  /// </summary>
  /// <remarks>
  /// This timestamp is updated whenever the interaction state of the game
  /// advances. Note that this timestamp does not necessarily increment with
  /// each game action.
  /// </remarks>
  public int Timestamp => @base.Timestamp;

  public GameZone Zone => new(@base.Zone);

  // IsExiledOnBattlefield
  // IsMutatedOnBattlefield
  // IsAbilityOnTheStack
  public GameZone ActualZone => new(@base.ActualZone);

  public GameZone PreviousZone => new(@base.PreviousZone);

  public IList<CardAction> Actions =>
    Map<IList, CardAction>(@base.Actions);

  public IList<CardAction> PendingTargets =>
    Map<IList, CardAction>(@base.PendingTargets);

  public IEnumerable<GameCardAssociation> Associations =>
    Map<GameCardAssociation>(@base.Associations);

  public IEnumerable<GameCard> AttackingOrders =>
    ((IEnumerable<OrderedCombatParticipant>)
     Map<OrderedCombatParticipant>(Unbind(@base).AttackingOrders))
      .OrderBy(item => item.Order)
      .Select(item => item.Target);

  public IEnumerable<GameCard> BlockingOrders =>
    ((IEnumerable<OrderedCombatParticipant>)
     Map<OrderedCombatParticipant>(Unbind(@base).BlockingOrders))
      .OrderBy(item => item.Order)
      .Select(item => item.Target);

  public IEnumerable<CardCounter> Counters =>
    Map<CardCounter>(Unbind(@base).Counters,
      new Func<dynamic, CardCounter>(Cast<CardCounter>));

  public GamePlayer Owner => new(@base.Owner);

  public GamePlayer Controller => new(@base.Controller);

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
  // DuelSceneCardViewModel properties
  //

  // public EventProxy ActionMouseEnterCommand =
  //   new(/* DuelSceneCardViewModel */ cardModel, nameof(ActionMouseEnterCommand));

  //
  // IGameCard wrapper events
  //

  public EventProxy<GameCardEventArgs> IsAttackingChanged =
    new(/* IGameCard */ gameCard, nameof(IsAttackingChanged));

  public EventProxy<GameCardEventArgs> IsBlockingChanged =
    new(/* IGameCard */ gameCard, nameof(IsBlockingChanged));

  public EventProxy<GameCardEventArgs> IsTappedChanged =
    new(/* IGameCard */ gameCard, nameof(IsTappedChanged));

  public EventProxy<GameCardEventArgs> DamageChanged =
    new(/* IGameCard */ gameCard, nameof(DamageChanged));

  public EventProxy<GameCardEventArgs> PowerChanged =
    new(/* IGameCard */ gameCard, nameof(PowerChanged));

  public EventProxy<GameCardEventArgs> ToughnessChanged =
    new(/* IGameCard */ gameCard, nameof(ToughnessChanged));

  public EventProxy<GameCardEventArgs> ZoneChanged =
    new(/* IGameCard */ gameCard, nameof(ZoneChanged));

  public EventProxy<GameCardEventArgs> AbilitiesChanged =
    new(/* IGameCard */ gameCard, nameof(AbilitiesChanged));

  public EventProxy<GameCardEventArgs> TypesChanged =
    new(/* IGameCard */ gameCard, nameof(TypesChanged));
}
