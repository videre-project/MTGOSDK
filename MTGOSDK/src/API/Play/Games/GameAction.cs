/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public abstract class GameAction : DLRWrapper<IGameAction>
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

  public void SetTimestamp(uint timestamp) => this._timestamp = timestamp;

  public static readonly Func<dynamic, GameAction> GameActionFactory =
    new(FromGameAction);

  private static GameAction FromGameAction(dynamic gameAction)
  {
    // Try to resolve the remote type name for dynamic remote objects.
    string actionType = "Unknown";
    try
    {
      actionType = gameAction.GetType().Name;
    }
    catch { }

    // Map each action type to its corresponding wrapper class.
    dynamic actionObject = null!;
    try
    {
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
        case "NumericAction":
          actionObject = new NumericAction(gameAction);
          break;
        case "OrderingAction":
          actionObject = new OrderingAction(gameAction);
          break;
        case "PrimitiveAction":
          actionObject = new PrimitiveAction(gameAction);
          break;
        case "SelectFromListAction":
          actionObject = new SelectFromListAction(gameAction);
          break;
        case "SelectPlayerAction":
          actionObject = new SelectPlayerAction(gameAction);
          break;
        case "ToggleMessageAction":
          actionObject = new ToggleMessageAction(gameAction);
          break;
        case "UndoAction":
          actionObject = new UndoAction(gameAction);
          break;
        default:
          // Try fallback for DynamicRemoteObject if GetType().Name failed to resolve correctly.
          try
          {
            if (gameAction is DynamicRemoteObject dro)
            {
              actionType = dro.__type.Name;

              // Avoid infinite recursion if the fallback also resolves to something we've seen
              if (actionType != "Unknown" && actionType != "DynamicRemoteObject")
              {
                return FromGameActionInternal(gameAction, actionType);
              }
            }
          }
          catch { }
          break;
      }
    }
    catch (Exception e)
    {
      Log.Error("GameActionFactory failed for type {0}: {1}", actionType, e.Message);
      Log.Debug(e.ToString());
      return null!;
    }

    return actionObject;
  }

  private static GameAction FromGameActionInternal(dynamic gameAction, string actionType)
  {
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
      case "NumericAction":
        actionObject = new NumericAction(gameAction);
        break;
      case "OrderingAction":
        actionObject = new OrderingAction(gameAction);
        break;
      case "PrimitiveAction":
        actionObject = new PrimitiveAction(gameAction);
        break;
      case "SelectFromListAction":
        actionObject = new SelectFromListAction(gameAction);
        break;
      case "SelectPlayerAction":
        actionObject = new SelectPlayerAction(gameAction);
        break;
      case "ToggleMessageAction":
        actionObject = new ToggleMessageAction(gameAction);
        break;
      case "UndoAction":
        actionObject = new UndoAction(gameAction);
        break;
    }
    return actionObject;
  }
}
