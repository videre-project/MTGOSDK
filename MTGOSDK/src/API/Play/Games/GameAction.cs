/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class GameAction(dynamic gameAction) : DLRWrapper<IGameAction>
{
  /// <summary>
  /// Stores an internal reference to the IGameAction object.
  /// </summary>
  internal override dynamic obj => Bind<IGameAction>(gameAction);

  //
  // IGameAction wrapper properties
  //

  /// <summary>
  /// The public name of the game action.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The type of game action (e.g. ChooseOption, OrderTargets, PayMana, etc.).
  /// </summary>
  /// <remarks>
  /// Requires the <c>MTGOSDK.Ref.dll</c> reference assembly.
  /// </remarks>
  public ActionType Type =>
    Cast<ActionType>(Unbind(@base).ActionType);

  /// <summary>
  /// The bound hotkey for the game action.
  /// </summary>
  public uint HotKey => @base.HotKey;

  /// <summary>
  /// The available modifiers for yielding priority (e.g. YieldThroughTurn, etc.).
  /// </summary>
  /// <remarks>
  /// Requires the <c>MTGOSDK.Ref.dll</c> reference assembly.
  /// </remarks>
  public ActionModifiers AvailableModifiers =>
    Cast<ActionModifiers>(Unbind(@base).AvailableModifiers);

  /// <summary>
  /// The selected priority modifiers applied to the game action.
  /// </summary>
  /// <remarks>
  /// Requires the <c>MTGOSDK.Ref.dll</c> reference assembly.
  /// </remarks>
  public ActionModifiers SelectedModifiers =>
    Cast<ActionModifiers>(Unbind(@base).SelectedModifiers);

  /// <summary>
  /// Whether the game action is the default selectable action.
  /// </summary>
  public bool IsDefault => @base.IsDefault;
}
