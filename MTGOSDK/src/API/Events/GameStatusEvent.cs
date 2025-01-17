/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;


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
  /// Delegate type for subscribing to Game events updating the game status.
  /// </summary>
  public delegate void GameStatusEventCallback(GameStatusEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Game events updating the game status.
  /// </summary>
  public class GameStatusEventArgs(dynamic args) : GameEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The previous game status.
    /// </summary>
    public GameStatus OldStatus => Cast<GameStatus>(@base.GameState);

    /// <summary>
    /// The new game status.
    /// </summary>
    public GameStatus NewStatus => Cast<GameStatus>(@base.GameState);
  }
}
