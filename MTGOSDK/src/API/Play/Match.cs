/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Enums;


namespace MTGOSDK.API.Play;
using static MTGOSDK.API.Events;

public sealed class Match(dynamic match) : Event<Match>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  internal override Type type => typeof(IMatch);

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
  public Guid MatchToken => Cast<Guid>(Unbind(@base).MatchToken);

  /// <summary>
  /// The state of the match (i.e. "Joined", "GameStarted", "Sideboarding", etc.)
  /// </summary>
  public MatchState State => Cast<MatchState>(Unbind(@base).Status);

  /// <summary>
  /// Whether the match has been completed.
  /// </summary>
  public bool IsComplete => Unbind(@base).IsCompleted ||
    State == (MatchState.MatchCompleted | MatchState.GameClosed);

  /// <summary>
  /// The user who created the match.
  /// </summary>
  public User? Creator => Optional<User>(@base.Creator);

  /// <summary>
  /// The user being challenged to the match.
  /// </summary>
  public User? ChallengeReceiver => Optional<User>(@base.ChallengeReceiver);

  /// <summary>
  /// The challenge text sent to the challenge receiver.
  /// </summary>
  public string ChallengeText => @base.ChallengeText;

  /// <summary>
  /// The games played in this match.
  /// </summary>
  public IList<Game> Games => Map<IList, Game>(@base.Games);

  /// <summary>
  /// The current game being played.
  /// </summary>
  public Game? CurrentGame => Optional<Game>(@base.CurrentGame);

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
  public IList<User> WinningPlayers => Map<IList, User>(@base.WinningPlayers);

  /// <summary>
  /// The player(s) who lost the match.
  /// </summary>
  public IList<User> LosingPlayers => Map<IList, User>(@base.LosingPlayers);

  //
  // IMatch wrapper events
  //

  public EventProxy<GameEventArgs> GameEnded =
    new(/* IMatch */ match, nameof(GameEnded));

  public EventProxy<GameEventArgs> CurrentGameChanged =
    new(/* IMatch */ match, nameof(CurrentGameChanged));

  public EventProxy ChallengeDeclined =
    new(/* IMatch */ match, nameof(ChallengeDeclined));

  public EventProxy<CountdownEventArgs> Countdown =
    new(/* IMatch */ match, nameof(Countdown));

  public EventProxy CountdownCancelled =
    new(/* IMatch */ match, nameof(CountdownCancelled));

  public EventProxy DeckForSideboardingChanged =
    new(/* IMatch */ match, nameof(DeckForSideboardingChanged));

  public EventProxy<MatchStatusEventArgs> MatchStatusChanged =
    new(/* IMatch */ match, nameof(MatchStatusChanged));

  public EventProxy<MatchErrorEventArgs> MatchError =
    new(/* IMatch */ match, nameof(MatchError));
}
