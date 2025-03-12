/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public abstract class GameAction : DLRWrapper<IGameAction>
{
  //
  // IGameAction wrapper properties
  //

  /// <summary>
  /// The public name of the game action.
  /// </summary>
  public string Name => Unbind(@base).Name;

  public int ActionId => (int)Unbind(@base).ActionFlags;

  /// <summary>
  /// The interaction timestamp of the game action.
  /// </summary>
  [NonSerializable]
  public uint Timestamp => Unbind(@base).Timestamp;

  /// <summary>
  /// The type of game action (e.g. ChooseOption, OrderTargets, PayMana, etc.).
  /// </summary>
  public ActionType Type =>
    Cast<ActionType>(Unbind(@base).ActionType);

  /// <summary>
  /// The bound hotkey for the game action.
  /// </summary>
  [NonSerializable]
  public uint HotKey => Unbind(@base).HotKey;

  /// <summary>
  /// The available modifiers for yielding priority (e.g. YieldThroughTurn, etc.).
  /// </summary>
  [NonSerializable]
  public ActionModifiers AvailableModifiers =>
    Cast<ActionModifiers>(Unbind(@base).AvailableModifiers);

  /// <summary>
  /// The selected priority modifiers applied to the game action.
  /// </summary>
  [NonSerializable]
  public ActionModifiers SelectedModifiers =>
    Cast<ActionModifiers>(Unbind(@base).SelectedModifiers);

  /// <summary>
  /// Whether the game action is the default selectable action.
  /// </summary>
  [NonSerializable]
  public bool IsDefault => Unbind(@base).IsDefault;

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

  public static readonly Func<dynamic, GameAction> GameActionFactory =
    new(FromGameAction);

  private static GameAction FromGameAction(dynamic gameAction)
  {
    string actionType = gameAction.GetType().Name;

    // Map each action type to its corresponding wrapper class.
    dynamic actionObject = null!;
    switch (actionType)
    {
      case "CardAction":
        actionObject = new CardAction(gameAction);
        break;
      case "CardSelectorAction":
        actionObject = new CardSelectorAction(gameAction);
        break;
      case "CardWishAction":
        actionObject = new CardWishAction(gameAction);
        break;
      case "CombatDamageAssignmentAction":
        actionObject = new CombatDamageAssignmentAction(gameAction);
        break;
      case "ConcedeGameAction":
        actionObject = new ConcedeGameAction(gameAction);
        break;
      case "DistributingCardAction":
        actionObject = new DistributingCardAction(gameAction);
        break;
      case "FunctionKeyMessageAction":
        actionObject = new FunctionKeyMessageAction(gameAction);
        break;
      case "LocalAction":
        actionObject = new LocalAction(gameAction);
        break;
      // case "ManaPoolAction":
      //   actionObject = new ManaPoolAction(gameAction);
      //   break;
      case "NumericAction":
        actionObject = new NumericAction(gameAction);
        break;
      case "OrderingAction":
        actionObject = new OrderingAction(gameAction);
        break;
      case "PrimitiveAction":
        actionObject = new PrimitiveAction(gameAction);
        break;
      // case "PrimitiveWithAlternatesAction":
      //   actionObject = new PrimitiveWithAlternatesAction(gameAction);
      //   break;
      // case "RemoveYieldAction":
      //   actionObject = new RemoveYieldAction(gameAction);
      //   break;
      case "SelectFromListAction":
        actionObject = new SelectFromListAction(gameAction);
        break;
      case "SelectPlayerAction":
        actionObject = new SelectPlayerAction(gameAction);
        break;
      // case "SimpleMessageAction":
      //   actionObject = new SimpleMessageAction(gameAction);
      //   break;
      case "ToggleMessageAction":
        actionObject = new ToggleMessageAction(gameAction);
        break;
      case "UndoAction":
        actionObject = new UndoAction(gameAction);
        break;
      // default:
      //   throw new InvalidOperationException($"Unknown action type: {actionType}");
    }
    // Log.Trace("Created new {Type} object for '{ActionObject}'.",
    //     actionType.GetType().Name, actionObject);

    return actionObject;
  }
}
