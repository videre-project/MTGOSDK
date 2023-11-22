/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK;
using GameCard = MTGOSDK.API.Play.GameCard;

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
  /// Delegate type for subscribing to GameCard events.
  /// </summary>
  public delegate void GameCardEventCallback(GameCardEventArg args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on GameCard events.
  /// </summary>
  public class GameCardEventArg(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.GameCardEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The game card instance that triggered the event.
    /// </summary>
    public GameCard Card => new(@base.Card);
  }
}
