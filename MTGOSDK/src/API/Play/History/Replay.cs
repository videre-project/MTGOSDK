/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play.ReplayGameEvent;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Represents a replay game event, describing the state of a replay.
/// </summary>
public class Replay(dynamic replayEvent) : DLRWrapper<IReplayGameEvent>
{
  /// <summary>
  /// Stores an internal reference to the IReplayGameEvent object.
  /// </summary>
  internal override dynamic obj => Bind<IReplayGameEvent>(replayEvent);

  //
  // IReplayGameEvent wrapper properties
  //

  /// <summary>
  /// The game object that this event is associated with.
  /// </summary>
  public Game Game => new(@base);

  /// <summary>
  /// The current state of the replay (e.g. "RequestSent", "Connecting", etc.).
  /// </summary>
  public ReplayState State => Cast<ReplayState>(Unbind(@base).ReplayState);

  /// <summary>
  /// The host game server ID replaying the game.
  /// </summary>
  public int HostGshServerId => @base.HostGshServerId;

  //
  // IReplayGameEvent wrapper methods
  //

  // public void ExecuteAction(GameAction action) =>
  //   Unbind(@base).ExecuteAction(action.@base);
}
