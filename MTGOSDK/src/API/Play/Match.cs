/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Enums;


namespace MTGOSDK.API.Play;

public sealed class Match(dynamic match) : Event<IMatch>
{
  /// <summary>
  /// Stores an internal reference to the IMatch object.
  /// </summary>
  internal override dynamic obj => Bind<IMatch>(match);

  //
  // IMatch wrapper properties
  //

  /// <summary>
  /// The unique ID for this match.
  /// </summary>
  public int MatchId => @base.MatchId;

  /// <summary>
  /// The match's session token.
  /// </summary>
  public Guid MatchToken => Cast<Guid>(@base.MatchToken);

  /// <summary>
  /// The status of the match (i.e. "Joined", "GameStarted", "Sideboarding", etc.)
  /// </summary>
  /// <remarks>
  /// Requires the <c>WotC.MtGO.Client.Model.Play.Enums</c> reference assembly.
  /// </remarks>
  internal MatchStatuses Status => Cast<MatchStatuses>(Unbind(@base).Status);

  /// <summary>
  /// The user who created the match.
  /// </summary>
  public User Creator => new(@base.Creator);

  /// <summary>
  /// The user being challenged to the match.
  /// </summary>
  public User ChallengeReceiver => new(@base.ChallengeReceiver);

  /// <summary>
  /// The challenge text sent to the challenge receiver.
  /// </summary>
  public string ChallengeText => @base.ChallengeText;

  /// <summary>
  /// The games played in this match.
  /// </summary>
  public IEnumerable<Game> Games => Map<Game>(@base.Games);

  /// <summary>
  /// The current game being played.
  /// </summary>
  public Game CurrentGame => new(@base.CurrentGame);

  /// <summary>
  /// The start time of the match.
  /// </summary>
  public DateTime StartTime => @base.StartTime;

  /// <summary>
  /// The ending time of the match.
  /// </summary>
  public DateTime EndTime => @base.EndTime;

  /// <summary>
  /// The end time of the sideboarding phase.
  /// </summary>
  public DateTime SideboardingEnds => @base.SideboardingEnds;

  // public Deck RegisteredDeck => new(@base.DeckForSideboarding); // Validate?

  /// <summary>
  /// The player(s) who won the match.
  /// </summary>
  public IEnumerable<User> WinningPlayers => Map<User>(@base.WinningPlayers);

  /// <summary>
  /// The player(s) who lost the match.
  /// </summary>
  public IEnumerable<User> LosingPlayers => Map<User>(@base.LosingPlayers);
}
