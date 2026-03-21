/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Reflection;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.InProgressGameEvent;


namespace MTGOSDK.API.Play.Games;
using static MTGOSDK.API.Events;

[NonSerializable]
public sealed class GameCard(dynamic gameCard) : DLRWrapper<IGameCard>
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj =>
    gameCard is GameCardPartial partial
      ? partial
      : Bind<IGameCard>(gameCard);

  public readonly struct OrderedCombatParticipant(int order, GameCard target)
  {
    public readonly int Order = order;
    public readonly GameCard Target = target;
  }

  internal Game GameInterface => new(@base.GameInterface);

  private static ICardZone? GetCardZone(
    Game game,
    GameCard card,
    int? zoneId,
    int? ownerId = null)
  {
    if (zoneId == null) return null;

    var zone = Unbind(card).GetSharedCardZone((uint)zoneId);
    if (zone == null && ownerId != null)
    {
      dynamic owner = Unbind(game).PlayersInServerOrder[ownerId];
      zone = Unbind(card).GetPlayerCardZone((uint)zoneId, owner);
    }
    if (zone == null) return null;

    return Bind<ICardZone>(zone);
  }

  private static MethodInfo? s_gcCreateMethod;

  public static GameCard Create(
    Game game,
    PropertyContainer properties,
    int? previousZone = null)
  {
    //
    // This tells MTGO to ignore tracking this card as part of its visual zone collections.
    // This is necessary because we are creating these cards as static snapshots of the
    // game state, and we don't want MTGO to try and manage them in its own collections
    // as it may confuse the WPF binding system.
    //
    properties[MagicProperty.SPLITPARENT_ID] = -2;

    // Create new GameCard object with state properties populated.
    s_gcCreateMethod ??= RemoteClient.GetMethod<WotC.MtGO.Client.Model.Play.GameCard>("Create");
    dynamic source = s_gcCreateMethod.Invoke(null, [-1, Unbind(game), Unbind(properties)]);
    source.ResolvePropertiesButDeferUpdatingAssociations();
    var gameCard = new GameCard(source);

    RemoteClient.SetField(source, "m_zone", source.ActualZone);
    if (previousZone != null)
    {
      int? ownerId = properties[MagicProperty.OWNER];
      var prevZone = GetCardZone(game, gameCard, previousZone, ownerId);
      RemoteClient.SetProperty(source, "PreviousZone", Unbind(prevZone));
    }

    return gameCard;
  }

  /// <summary>
  /// Creates a GameCard backed by a local PropertyContainer snapshot.
  /// No remote IPC call to MTGO.
  /// </summary>
  /// <param name="properties">The parsed MagicProperty dictionary.</param>
  /// <param name="game">The raw MTGO Game object (for player/zone resolution).</param>
  /// <param name="previousZoneId">Optional raw zone ID for PreviousZone resolution.</param>
  public static GameCard FromProperties(
    PropertyContainer properties,
    dynamic game,
    int? previousZoneId = null)
  {
    var snapshot = new GameCardPartial(properties, game, previousZoneId);
    return new GameCard(snapshot);
  }

  //
  // IGameCard wrapper properties
  //

  /// <summary>
  /// The unique identifier for this card instance.
  /// </summary>
  /// <remarks>
  /// Retrieved from the <c>MagicProperty.THINGNUMBER</c> property.
  /// </remarks>
  public int Id => Unbind(this).Id;

  /// <summary>
  /// The unique card texture number for the card object.
  /// </summary>
  public int CTN => Unbind(this).CTN;

  /// <summary>
  /// The source ID or 'thing' number of the card.
  /// </summary>
  /// <remarks>
  /// Retrieved from the <c>MagicProperty.SRC_THING_ID</c> property.
  /// </remarks>
  public int SourceId => @base.SourceId;

  /// <summary>
  /// The card name.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The associated card definition.
  /// </summary>
  [NonSerializable]
  public Card Definition => new(Unbind(this).Definition);

  /// <summary>
  /// The latest interaction timestamp associated with the card.
  /// </summary>
  /// <remarks>
  /// This timestamp is updated whenever the interaction state of the game
  /// advances. Note that this timestamp does not necessarily increment with
  /// each game action.
  /// </remarks>
  public int Timestamp => @base.Timestamp;

  /// <summary>
  /// The zone in which the card is currently located.
  /// </summary>
  public GameZone? Zone =>
    gameCard is GameCardPartial partial
      ? partial.GetZoneWrapper()
      : Optional<GameZone>(@base.Zone);

  // IsExiledOnBattlefield
  // IsMutatedOnBattlefield
  // IsAbilityOnTheStack
  public GameZone? ActualZone =>
    gameCard is GameCardPartial partialActual
      ? partialActual.GetZoneWrapper()
      : Optional<GameZone>(@base.ActualZone);

  /// <summary>
  /// The previous zone in which the card was located.
  /// </summary>
  /// <remarks>
  /// The card's <c>Id</c> and <c>SourceId</c> properties are updated whenever
  /// the card moves between zones. This property is used to track the card's
  /// previous zone, where the card's <c>Id</c> becomes the new <c>SourceId</c>
  /// to track the card's movement history.
  /// </remarks>
  public GameZone? PreviousZone =>
    gameCard is GameCardPartial partialPrevious
      ? partialPrevious.GetPreviousZoneWrapper()
      : Optional<GameZone>(@base.PreviousZone);

  public IList<CardAction> Actions =>
    Map<IList, CardAction>(@base.Actions);

  public IList<CardAction> PendingTargets =>
    Map<IList, CardAction>(@base.PendingTargets);

  public IEnumerable<GameCardAssociation> Associations =>
    Map<GameCardAssociation>(@base.Associations);

  public IEnumerable<GameCard> AttackingOrders
  {
    get
    {
      if (@base is GameCardPartial partial)
        return partial.AttackingOrders.Select(p => p.Target);

      var attackers = new List<OrderedCombatParticipant>();
      foreach (var item in Unbind(this).AttackingOrders)
      {
        int order = item.Order;
        GameCard target = new(item.Target);
        attackers.Add(new OrderedCombatParticipant(order, target));
      }

      return attackers.OrderBy(a => a.Order).Select(a => a.Target);
    }
  }

  public IEnumerable<GameCard> BlockingOrders
  {
    get
    {
      if (@base is GameCardPartial partial)
        return partial.BlockingOrders.Select(p => p.Target);

      var blockers = new List<OrderedCombatParticipant>();
      foreach (var item in Unbind(this).BlockingOrders)
      {
        int order = item.Order;
        GameCard target = new(item.Target);
        blockers.Add(new OrderedCombatParticipant(order, target));
      }

      return blockers.OrderBy(b => b.Order).Select(b => b.Target);
    }
  }

  public IEnumerable<CardAbility> Abilities =>
    Map<IList, CardAbility>(Unbind(this).Abilities);

  public IEnumerable<CardCounter> Counters =>
    Map<IList, CardCounter>(Unbind(this).Counters);

  public GamePlayer Owner =>
    gameCard is GameCardPartial partialOwner
      ? partialOwner.GetOwnerWrapper()
      : new(@base.Owner);

  public GamePlayer Controller =>
    gameCard is GameCardPartial partialController
      ? partialController.GetControllerWrapper()
      : new(@base.Controller);

  public GamePlayer? Protector =>
    gameCard is GameCardPartial partialProtector
      ? partialProtector.GetProtectorWrapper()
      : Optional<GamePlayer>(@base.Protector);

  public GameCard? OtherFace => Optional<GameCard>(@base.OtherFace);

  public int Power => @base.Power;

  public int Toughness => @base.Toughness;

  public int Loyalty => @base.Loyalty;

  public int Damage => @base.Damage;

  public int CurrentLevel => @base.CurrentLevel;

  public int CurrentDungeonRoom => @base.CurrentDungeonRoom;

  public int CurrentChapter => @base.CurrentChapter;

public bool HasSummoningSickness => (bool?)(@base.HasSummoningSickness) ?? false;

    public bool IsNewlyControlled => (bool?)(@base.IsNewlyControlled) ?? false;

    public bool IsAttacking => (bool?)(@base.IsAttacking) ?? false;

    public bool IsBlocking => (bool?)(@base.IsBlocking) ?? false;

    public bool IsBlocked => (bool?)(@base.IsBlocked) ?? false;

    public bool IsTapped => (bool?)(@base.IsTapped) ?? false;

    public bool IsFlipped => (bool?)(@base.IsFlipped) ?? false;

    public bool IsCompanion => (bool?)(@base.IsCompanion) ?? false;

    public bool IsEmblem => (bool?)(@base.IsEmblem) ?? false;

    public bool IsActivatedAbility => (bool?)(@base.IsActivatedAbility) ?? false;

    public bool IsTriggeredAbility => (bool?)(@base.IsTriggeredAbility) ?? false;

    public bool IsDelayedTrigger => (bool?)(@base.IsDelayedTrigger) ?? false;

    public bool IsReplacementEffect => (bool?)(@base.IsReplacementEffect) ?? false;

    public bool IsYieldAbility => (bool?)(@base.IsYieldAbility) ?? false;

    public bool HasAutoTargets => (bool?)(@base.HasAutoTargets) ?? false;

  //
  // IGameCard wrapper methods
  //

  public override string ToString() =>
    string.Format("{0} (ID: {1}, SourceId: {2})", Name, Id, SourceId);

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
