/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Enums;
using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Tournaments;

public sealed class MatchStandingRecord(dynamic matchStandingRecord)
    : DLRWrapper<IMatchStandingRecord>
{
  /// <summary>
  /// Stores an internal reference to the IMatchStandingRecord object.
  /// </summary>
  internal override dynamic obj => matchStandingRecord;

  //
  // IMatchStandingRecord wrapper properties
  //

  /// <summary>
  /// The ID of the match.
  /// </summary>
  [Default(-1)]
  public int Id => @base.Id;

  /// <summary>
  /// The round number of the match.
  /// </summary>
  public int Round => @base.Round;

  /// <summary>
  /// The status of the match (i.e. "Joined", "GameStarted", "Sideboarding", etc.)
  /// </summary>
  /// <remarks>
  /// Requires the <c>MTGOSDK.Ref.dll</c> reference assembly.
  /// </remarks>
  public MatchStatuses Status => Cast<MatchStatuses>(Unbind(@base).Status);

  /// <summary>
  /// Whether the player has been assigned a bye.
  /// </summary>
  public bool HasBye => @base.HasBye;

  /// <summary>
  /// The user objects of both players.
  /// </summary>
  public IEnumerable<User> Players
  {
    get
    {
      foreach(var player in @base.Users)
        yield return new User(player.Name);
    }
  }

  /// <summary>
  /// The IDs of the winning player(s).
  /// </summary>
  public IList<int> WinningPlayerIds // FIXME: .NET 8 DLR regression
  {
    get
    {
      IList<int> winningPlayerIds = new List<int>();
      foreach(var playerId in @base.WinningPlayerIds)
        winningPlayerIds.Add(playerId);

      return winningPlayerIds;
    }
  }

  /// <summary>
  /// The IDs of the losing player(s).
  /// </summary>
  public IList<int> LosingPlayerIds // FIXME: .NET 8 DLR regression
  {
    get
    {
      IList<int> losingPlayerIds = new List<int>();
      foreach(var playerId in @base.LosingPlayerIds)
        losingPlayerIds.Add(playerId);

      return losingPlayerIds;
    }
  }

  /// <summary>
  /// The results of each game in the match.
  /// </summary>
  public IEnumerable<GameStandingRecord> GameStandingRecords =>
    Map<GameStandingRecord>(@base.GameStandingRecords);
}
