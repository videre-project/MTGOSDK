/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Linq;
using System.Collections.Generic;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
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
  /// <remarks>
  /// Requires the <c>WotC.MTGO.Common</c> reference assembly.
  /// </remarks>
  public GameState GateState =>
    Cast<GameState>(Unbind(@base).GameState);

  /// <summary>
  /// The elapsed time to completion since the game started.
  /// </summary>
  public TimeSpan CompletedDuration =>
    Cast<TimeSpan>(Unbind(@base).CompletedDuration);

  /// <summary>
  /// The IDs of the winning player(s).
  /// </summary>
  public IList<int> WinnerIds // FIXME: .NET 8 DLR regression
  {
    get
    {
      var winnerIds = new List<int>();
      foreach(var winnerId in @base.WinnerIds)
      {
        winnerIds.Add(winnerId);
      }
      return winnerIds;
    }
  }
  // public IEnumerable<int> WinnerIds => @base.WinnerIds;
}
