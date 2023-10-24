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


namespace MTGOSDK.API.Play.Events.Tournaments;

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
  public int Id => @base.Id;

  /// <summary>
  /// The round number of the match.
  /// </summary>
  public int Round => @base.Round;

  /// <summary>
  /// The status of the match (i.e. "Joined", "GameStarted", "Sideboarding", etc.)
  /// </summary>
  public MatchStatuses Status => @base.Status;

  /// <summary>
  /// Whether the player has been assigned a bye.
  /// </summary>
  public bool HasBye => @base.HasBye;

  /// <summary>
  /// The user objects of both players.
  /// </summary>
  public User[] Players
  {
    get
    {
      var players = new List<User>();
      foreach(var player in @base.Users)
        players.Add(new User(player.Id, player.Name));
      return players.ToArray();
    }
  }

  /// <summary>
  /// The IDs of the winning player(s).
  /// </summary>
  public IList<int> WinningPlayerIds
  {
    get
    {
      var ids = new List<int>();
      foreach(var id in @base.WinningPlayerIds)
        ids.Add(id);
      return ids;
    }
  }

  /// <summary>
  /// The IDs of the losing player(s).
  /// </summary>
  public IList<int> LosingPlayerIds
  {
    get
    {
      var ids = new List<int>();
      foreach(var id in @base.LosingPlayerIds)
        ids.Add(id);
      return ids;
    }
  }

  /// <summary>
  /// The results of each game in the match.
  /// </summary>
  public IEnumerable<GameStandingRecord> GameStandingRecords
  {
    get
    {
      foreach (var gameRecord in @base.GameStandingRecords)
        yield return new GameStandingRecord(gameRecord);
    }
  }
}
