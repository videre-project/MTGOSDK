/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;
using static MTGOSDK.API.Events;

[NonSerializable]
public sealed class GameCard(dynamic gameCard) : DLRWrapper<IGameCard>
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj =>
    Unbind(gameCard) is GameCardPartial partial
      ? partial
      : Bind<IGameCard>(gameCard);

  internal Game GameInterface => new(@base.GameInterface);

  //
  // IGameCard wrapper properties
  //

  /// <summary>
  /// The native thing number for this card. Auxiliary cards may report -1.
  /// </summary>
  /// <remarks>
  /// Retrieved from the <c>MagicProperty.THINGNUMBER</c> property.
  /// </remarks>
  public int Id => Unbind(this).Id;

  /// <summary>
  /// The unique card texture number for the card object.
  /// </summary>
  /// <remarks>
  /// Uses the resolved definition. Cards without a definition retain their
  /// reported texture identity when no native definition is available.
  /// </remarks>
  public int CTN => Definition?.CTN ?? GetIntegerProperty(MagicProperty.CARDTEXTURE_NUMBER);

  /// <summary>
  /// The source ID or 'thing' number of the card.
  /// </summary>
  /// <remarks>
  /// Retrieved from the <c>MagicProperty.SRC_THING_ID</c> property.
  /// </remarks>
  public int SourceId => @base.SourceId;

  /// <summary>
  /// For parent cards (adventure/split/MDFC), the game IDs of the SUBC children.
  /// Returns 0 if no child is present at that index.
  /// </summary>
  public int SplitChildId0 => GetIntegerProperty(MagicProperty.GS_SPLITCARD_ID0);
  public int SplitChildId1 => GetIntegerProperty(MagicProperty.GS_SPLITCARD_ID1);

  /// <summary>
  /// The card name.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The rules text for this card or ability on the stack.
  /// Uses MTGO's priority chain: ACTION_SPELL_TEXT_STRING,
  /// then EFFECT_NAME_STRING, then REAL_ORACLETEXT_STRING.
  /// </summary>
  public string RulesText =>
    field ??= MTGOSDK.API.MtgoTextNormalizer.NormalizeText(@base.RulesText);

  /// <summary>
  /// The "blue text" — granted abilities and effects text shown in blue
  /// on the card in MTGO. Reads the native BlueTextString ability description.
  /// </summary>
  public string BlueText =>
    field ??= MTGOSDK.API.MtgoTextNormalizer.NormalizeText(
      ReadBlueText());

  /// <summary>
  /// The associated card definition, or null when a snapshot has none.
  /// </summary>
  [NonSerializable]
  public Card? Definition =>
    Unbind(this).Definition is object definition ? new Card(definition) : null;

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
    Unbind(this) is GameCardPartial partial
      ? partial.GetZoneWrapper()
      : Optional<GameZone>(@base.Zone);

  // IsExiledOnBattlefield
  // IsMutatedOnBattlefield
  // IsAbilityOnTheStack
  public GameZone? ActualZone =>
    Unbind(this) is GameCardPartial partialActual
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
    Unbind(this) is GameCardPartial partialPrevious
      ? partialPrevious.GetPreviousZoneWrapper()
      : Optional<GameZone>(@base.PreviousZone);

  public IList<CardAction> Actions =>
    Map<IList, CardAction>(@base.Actions);

  public IList<CardAction> PendingTargets =>
    Map<IList, CardAction>(@base.PendingTargets);

  public IEnumerable<GameCardAssociation> Associations =>
    Unbind(this) is GameCardPartial partial
      ? partial.Associations
      : Map<GameCardAssociation>(@base.Associations);

  /// <summary>
  /// The card ID this card is visually attached to (equipment target, aura
  /// target, mutate parent, encoded host, or exile-under parent).
  /// Returns 0 if not attached.
  /// </summary>
  /// <remarks>
  /// Uses the SDK's display-reference priority: SHOW_ATTACHED_TO → ATTACHED_TO_ID →
  /// MUTATE_PARENT_ID → ENCODED_ON.
  /// </remarks>
  public int AttachedToId => ReadAttachedToId();

  public IEnumerable<GameCard> AttackingOrders
  {
    get
    {
      if (@base is GameCardPartial partial)
        return partial.AttackingOrders.OrderBy(p => p.Order).Select(p => p.Target);

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
        return partial.BlockingOrders.OrderBy(p => p.Order).Select(p => p.Target);

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
    Map<IList, CardAbility>(Unbind(this).Abilities.Keys);

  public IEnumerable<CardCounter> Counters =>
    Map<IList, CardCounter>(Unbind(this).Counters);

  public string TypeLine =>
    Unbind(this) is GameCardPartial card
      ? (string)((dynamic)card).TypeLine
      : ReadTypeLine();

  public GamePlayer Owner =>
    Unbind(this) is GameCardPartial partialOwner
      ? partialOwner.GetOwnerWrapper()
      : new(@base.Owner);

  /// <summary>
  /// The raw owner player index from the card's properties.
  /// Captured cards use the detached value; live cards read the native player ID.
  /// </summary>
  public int OwnerIndex =>
    Unbind(this) is GameCardPartial partial
      ? partial.OwnerIndex
      : (int)@base.Owner.Id;

  public GamePlayer Controller =>
    Unbind(this) is GameCardPartial partialController
      ? partialController.GetControllerWrapper()
      : new(@base.Controller);

  /// <summary>
  /// The raw controller player index from the card's properties.
  /// Captured cards use the detached value; live cards read the native player ID.
  /// </summary>
  public int ControllerIndex =>
    Unbind(this) is GameCardPartial partial
      ? partial.ControllerIndex
      : (int)@base.Controller.Id;

  public GamePlayer? Protector =>
    Unbind(this) is GameCardPartial partialProtector
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

  public bool IsTransformed => GetBooleanProperty(MagicProperty.IS_TRANSFORMED);

  public bool IsCompanion => (bool?)(@base.IsCompanion) ?? false;

  public bool IsEmblem => (bool?)(@base.IsEmblem) ?? false;

  public bool IsActivatedAbility => (bool?)(@base.IsActivatedAbility) ?? false;

  public bool IsTriggeredAbility => (bool?)(@base.IsTriggeredAbility) ?? false;

  public bool IsDelayedTrigger => (bool?)(@base.IsDelayedTrigger) ?? false;

  public bool IsReplacementEffect => (bool?)(@base.IsReplacementEffect) ?? false;

  public bool IsYieldAbility => (bool?)(@base.IsYieldAbility) ?? false;

  public bool HasAutoTargets => (bool?)(@base.HasAutoTargets) ?? false;

  public bool IsToken => GetBooleanProperty(MagicProperty.IS_TOKEN);

  public bool IsLand => GetBooleanProperty(MagicProperty.IS_LAND);

  //
  // IGameCard wrapper methods
  //

  private int GetIntegerProperty(MagicProperty property) =>
    Unbind(this).GetIntegerProperty((WotC.MtGO.Client.Model.MagicProperty)(uint)property, 0);

  private bool GetBooleanProperty(MagicProperty property) =>
    Unbind(this).GetBooleanProperty((WotC.MtGO.Client.Model.MagicProperty)(uint)property);

  private string ReadBlueText()
  {
    dynamic abilities = Unbind(this).Abilities;
    var blueText = (WotC.MtGO.Client.Model.MagicCardAbility)(int)CardAbility.BlueTextString;
    return abilities.ContainsKey(blueText) ? (string)abilities[blueText].Name : string.Empty;
  }

  private int ReadAttachedToId()
  {
    foreach (var property in new[] { MagicProperty.SHOW_ATTACHED_TO,
      MagicProperty.ATTACHED_TO_ID, MagicProperty.MUTATE_PARENT_ID, MagicProperty.ENCODED_ON })
    {
      int id = GetIntegerProperty(property);
      if (id > 0) return id;
    }
    return 0;
  }

  private string ReadTypeLine()
  {
    var batch = new RemoteBatch();
    var method = new RemoteBatchMethod(
      new("Mtgo.Shared.Parser.Attributes.Helpers.MagicPropertyHelper", "Mtgo.Shared.Parser.Attributes"),
      "ConstructCardTypeText",
      new RemoteBatchType("WotC.MtGO.Client.Model.IPropertyHolder", "Mtgo.Shared.Parser.Attributes"));
    batch.Output("typeLine", batch.CallStatic(method, batch.Input((object)Unbind(this))));
    return batch.Execute(RemoteClient.@client).Values["typeLine"].GetScalar<string>();
  }

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

  /// <summary>
  /// Raised when a card's captured action list changes.
  /// </summary>
  /// <remarks>
  /// The card belongs to the correlated gameplay snapshot, and the actions come
  /// from its completed UI capture. Removing a card reports an empty action list
  /// with its last captured state. Subscribing activates game-state observation.
  /// </remarks>
  public static event Action<GameCard, IReadOnlyList<CardAction>> ActionsChanged
  {
    add
    {
      if (value is null) return;
      GameProcessor.CardActionsChanged += value;
      try
      {
        GameProcessor.ObserveCardActions();
      }
      catch
      {
        GameProcessor.CardActionsChanged -= value;
        throw;
      }
    }
    remove => GameProcessor.CardActionsChanged -= value;
  }
}
