/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK;
using GamePlayer = MTGOSDK.API.Play.GamePlayer;

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
  /// Delegate type for subscribing to GamePlayer events.
  /// </summary>
  public delegate void GamePlayerEventCallback(GamePlayerEventArg args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on GameCard events.
  /// </summary>
  public class GamePlayerEventArg(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.GamePlayerEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The game player instance that triggered the event.
    /// </summary>
    public GamePlayer Player => new(@base.Player);
  }
}