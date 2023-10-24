/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Events.Tournaments;

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
  public GameState GateState => @base.GameState;

  /// <summary>
  /// The elapsed time to completion since the game started.
  /// </summary>
  public TimeSpan CompletedDuration =>
    // Ensure the TimeSpan object is parsed correctly without
    // worrying about the culture of the current thread.
    TimeSpan.Parse(@base.CompletedDuration.ToString());

  /// <summary>
  /// The IDs of the winning player(s).
  /// </summary>
  public IList<int> WinnerIds
  {
    get
    {
      var ids = new List<int>();
      foreach(var id in @base.WinnerIds)
        ids.Add(id);
      return ids;
    }
  }
}
