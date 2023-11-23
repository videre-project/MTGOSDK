/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API;

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
  public class GameStateEventArgs(dynamic args) : GameEventArgs(null)
  {
    internal override dynamic obj => args;

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
