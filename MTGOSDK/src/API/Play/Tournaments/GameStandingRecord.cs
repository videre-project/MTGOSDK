/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Tournaments;

public sealed class GameStandingRecord(dynamic gameStandingRecord)
    : DLRWrapper<IGameStandingRecord>
{
  /// <summary>
  /// Stores an internal reference to the IGameStandingRecord object.
  /// </summary>
  internal override dynamic obj => gameStandingRecord;

  //
  // IGameStandingRecord wrapper properties
  //

  /// <summary>
  /// The ID of the game.
  /// </summary>
  public int Id => @base.Id;

  /// <summary>
  /// The game's current completion (i.e. "NotStarted", "Started", "Finished")
  /// </summary>
  public GameState GameState =>
    Retry(() => Cast<GameState>(Unbind(@base).GameState), GameState.Invalid);

  /// <summary>
  /// The elapsed time to completion since the game started.
  /// </summary>
  public TimeSpan? CompletedDuration =>
    Retry(() => Cast<TimeSpan>(Unbind(@base).CompletedDuration), null);

  /// <summary>
  /// The IDs of the winning player(s).
  /// </summary>
  public IList<int> WinnerIds => Map<IList, int>(@base.WinnerIds);
}
