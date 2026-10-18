/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public abstract class GameAction : DLRWrapper<IGameAction>, IPromptOption
{
  private uint _timestamp = 0;

  //
  // IGameAction wrapper properties
  //

  /// <summary>
  /// The public name of the game action.
  /// </summary>
  public string Name => Unbind(this).Name;

  public int ActionId => (int)Unbind(this).ActionFlags;

  /// <summary>
  /// The interaction timestamp of the game action.
  /// </summary>
  [NonSerializable]
  public uint Timestamp =>
    _timestamp > 0 ? _timestamp : Unbind(this).Timestamp;

  /// <summary>
  /// The client-side DateTime when this action was executed in the game client.
  /// Captured from the EventHookProxy's instance.__timestamp.
  /// </summary>
  [NonSerializable]
  public DateTime ClientTimestamp { get; private set; }

  /// <summary>
  /// The type of game action (e.g. ChooseOption, OrderTargets, PayMana, etc.).
  /// </summary>
  public ActionType Type =>
    Cast<ActionType>(Unbind(this).ActionType);

  /// <summary>
  /// The bound hotkey for the game action.
  /// </summary>
  [NonSerializable]
  public uint HotKey => Unbind(this).HotKey;

  /// <summary>
  /// The available modifiers for yielding priority (e.g. YieldThroughTurn, etc.).
  /// </summary>
  [NonSerializable]
  public ActionModifiers AvailableModifiers =>
    Cast<ActionModifiers>(Unbind(this).AvailableModifiers);

  /// <summary>
  /// The selected priority modifiers applied to the game action.
  /// </summary>
  [NonSerializable]
  public ActionModifiers SelectedModifiers =>
    Cast<ActionModifiers>(Unbind(this).SelectedModifiers);

  /// <summary>
  /// Whether the game action is the default selectable action.
  /// </summary>
  [NonSerializable]
  public bool IsDefault => Unbind(this).IsDefault;

  /// <summary>
  /// Whether the game action is a local action client-side.
  /// </summary>
  [NonSerializable]
  public bool IsLocal
  {
    get
    {
      if (this.GetType() == typeof(CardAction) &&
          (this.Name.StartsWith("Always yield to ") ||
           this.Name.StartsWith("Yield to ")))
      {
        return true;
      }

      return false;
    }
  }

  // public EventProxy ModifiersChanged =
  //   new(/* IGameAction */ gameAction, nameof(ModifiersChanged));

  //
  // GameAction factory methods
  //

  public void SetTimestamp(uint timestamp) =>
    this._timestamp = timestamp;

  public void SetClientTimestamp(DateTime timestamp) =>
    this.ClientTimestamp = timestamp;

  public static readonly Func<dynamic, GameAction> GameActionFactory =
    new(FromGameAction);

  /// <summary>
  /// Builds the projection used to capture an action's scalar properties and the
  /// choices, cards and players referenced by each supported action type.
  /// </summary>
  internal static RemoteBatchProjection CreateCaptureProjection()
  {
    var scalar = RemoteBatchProjection.Value();
    var card = RemoteBatchProjection.Object(
      ("Id",                "Id",                scalar),
      ("SourceId",          "SourceId",          scalar),
      ("Name",              "Name",              scalar),
      ("IsTargeted",        "IsTargeted",        scalar),
      ("TargetInformation", "TargetInformation", scalar)).WithLogicalAddress();
    var player = RemoteBatchProjection.Object(
      ("User", "User", RemoteBatchProjection.Object(("Id", "Id", scalar))));
    var playerChoice = RemoteBatchProjection.Object(
      ("Name",  "Name",  scalar),
      ("Value", "Value", player));

    // Target collections can contain either cards or players. The card's logical
    // address distinguishes objects whose game IDs are shared or unassigned.
    card.RuntimeTypes["WotC.MtGO.Client.Model.Play.GamePlayer"] = player;
    var distribution = CreateScalarProjection(("Target", "Target", card));
    var targetSet = CreateScalarProjection(
      ("LegalTargets",   "LegalTargets",   RemoteBatchProjection.Sequence(card)),
      ("CurrentTargets", "CurrentTargets", RemoteBatchProjection.Sequence(card)),
      ("Distributions", "Distributions", RemoteBatchProjection.Object(
        ("Values", "Values", RemoteBatchProjection.Sequence(distribution)))));

    var cardAction = CreateActionProjection(
      ("Card",    "Card",    card),
      ("Targets", "Targets", RemoteBatchProjection.Sequence(targetSet)));

    const string actionNamespace = "WotC.MtGO.Client.Model.Play.Actions.";
    var action = CreateActionProjection();
    action.RuntimeTypes = new()
    {
      [actionNamespace + nameof(CardSelectorAction)] = CreateActionProjection(
        ("Choices", "Choices", RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
          ("CTN", "CTN", scalar), ("CanChoose", "CanChoose", scalar))))),

      [actionNamespace + nameof(SelectFromListAction)] = CreateActionProjection(
        ("AvailableItems", "AvailableItems",
          RemoteBatchProjection.Sequence(RemoteBatchProjection.ObjectScalars())),
        ("SelectedValue", "SelectedValue", RemoteBatchProjection.ObjectScalars())),

      [actionNamespace + nameof(SelectPlayerAction)] = CreateActionProjection(
        ("AvailablePlayers", "AvailablePlayers", RemoteBatchProjection.Sequence(playerChoice)),
        ("SelectedPlayer",   "SelectedPlayer",   playerChoice)),

      [actionNamespace + nameof(CardWishAction)] = CreateActionProjection(
        ("WishedCard", "WishedCard", RemoteBatchProjection.ObjectScalars())),

      [actionNamespace + nameof(CombatDamageAssignmentAction)] = CreateActionProjection(
        ("Source",        "Source",        card),
        ("Distributions", "Distributions", RemoteBatchProjection.Sequence(distribution))),

      [actionNamespace + nameof(CardAction)] = cardAction,
      [actionNamespace + nameof(DistributingCardAction)] = cardAction,

      [actionNamespace + nameof(OrderingAction)] = CreateActionProjection(
        ("Source",         "Source",         card),
        ("OrderedTargets", "OrderedTargets", RemoteBatchProjection.Sequence(card)))
    };
    return action;
  }

  /// <summary>
  /// Includes the runtime type's scalar properties alongside explicitly
  /// projected members, such as target collections.
  /// </summary>
  private static RemoteBatchProjection CreateScalarProjection(
    params (string Name, string Path, RemoteBatchProjection Projection)[] members)
  {
    var projection = RemoteBatchProjection.Object(members);
    projection.RuntimeScalars = true;
    return projection;
  }

  /// <summary>
  /// Includes the action timestamp and maps the native Hotkey member to the
  /// SDK's HotKey name.
  /// </summary>
  private static RemoteBatchProjection CreateActionProjection(
    params (string Name, string Path, RemoteBatchProjection Projection)[] members)
  {
    var projection = CreateScalarProjection(members);
    var scalar = RemoteBatchProjection.Value();
    projection.Members["Timestamp"] = new()
    {
      Path = "Timestamp",
      Projection = scalar
    };
    projection.Members["HotKey"] = new()
    {
      Path = "Hotkey",
      Projection = scalar
    };
    return projection;
  }

  /// <summary>
  /// Wraps a captured action and links its target sets back to that action.
  /// </summary>
  internal static GameAction MaterializeCaptured(RemoteBatchObject captured)
  {
    if (captured.Members.TryGetValue("Targets", out var targets))
    {
      var targetSets = ((object?[])targets!).Cast<RemoteBatchObject>();
      captured = captured.WithMember("Targets", targetSets
        .Select((set, index) => set
          .WithMember("Action", captured)
          .WithMember("Index", index))
        .ToArray());
    }

    return GameActionFactory(captured)
      ?? throw new NotSupportedException(
        "Unsupported captured action: " + captured.NativeType?.FullName);
  }

  private static GameAction FromGameAction(dynamic gameAction)
  {
    string actionType = "Unknown";
    try
    {
      actionType = gameAction is RemoteBatchObject captured
        ? captured.NativeType!.FullName.Split('.').Last()
        : gameAction is DynamicRemoteObject remote
        ? remote.__type.Name
        : gameAction.GetType().Name;
    }
    catch { }

    try
    {
      return actionType switch
      {
        nameof(CardAction)                   => new CardAction(gameAction),
        nameof(CardSelectorAction)           => new CardSelectorAction(gameAction),
        nameof(CardWishAction)               => new CardWishAction(gameAction),
        nameof(CombatDamageAssignmentAction) => new CombatDamageAssignmentAction(gameAction),
        nameof(ConcedeGameAction)            => new ConcedeGameAction(gameAction),
        nameof(DistributingCardAction)       => new DistributingCardAction(gameAction),
        nameof(FunctionKeyMessageAction)     => new FunctionKeyMessageAction(gameAction),
        nameof(LocalAction)                  => new LocalAction(gameAction),
        nameof(NumericAction)                => new NumericAction(gameAction),
        nameof(OrderingAction)               => new OrderingAction(gameAction),
        nameof(PrimitiveAction)              => new PrimitiveAction(gameAction),
        "PrimitiveWithAlternatesAction"       => new PrimitiveAction(gameAction),
        nameof(ManaPoolAction)               => new ManaPoolAction(gameAction),
        nameof(SelectFromListAction)         => new SelectFromListAction(gameAction),
        nameof(SelectPlayerAction)           => new SelectPlayerAction(gameAction),
        nameof(ToggleMessageAction)          => new ToggleMessageAction(gameAction),
        nameof(UndoAction)                   => new UndoAction(gameAction),
        _                                    => null!
      };
    }
    catch (Exception e)
    {
      Log.Error("GameActionFactory failed for type {0}: {1}", actionType, e.Message);
      Log.Debug(e.ToString());
      return null!;
    }
  }
}
