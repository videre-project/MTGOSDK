/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API;
using Game = MTGOSDK.API.Play.Games.Game;

/// <summary>
/// EventHandler wrapper types used by the API.
/// </summary>
/// <remarks>
/// This class contains wrapper types for events importable via
/// <br/>
/// <c>using static MTGOSDK.API.Events;</c>.
/// </remarks>
public sealed partial class Events
{
  //
  // EventHandler delegate types
  //

  /// <summary>
  /// Delegate type for subscribing to Game events updating the game state.
  /// </summary>
  public delegate void GameStateEventCallback(GameStateEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Game events updating the game state.
  /// </summary>
  public class GameStateEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.GameStateEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The game instance that triggered the event.
    /// </summary>
    public Game Game => new(@base.Game);

    /// <summary>
    /// The previous game state.
    /// </summary>
    public GameState OldState => Cast<GameState>(@base.GameState);

    /// <summary>
    /// The new game state.
    /// </summary>
    public GameState NewState => Cast<GameState>(@base.GameState);
  }
}
