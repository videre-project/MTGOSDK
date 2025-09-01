/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;

public sealed class Match(dynamic match) : Event
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IMatch);

  /// <summary>
  /// Stores an internal reference to the IMatch object.
  /// </summary>
  internal override dynamic obj => Bind<IMatch>(match);

  public new int Id => @base.MatchId;

  public new Guid Token => Cast<Guid>(Unbind(@base).MatchToken);

  //
  // IMatch wrapper properties
  //

  /// <summary>
  /// The state of the match (i.e. "Joined", "GameStarted", "Sideboarding", etc.)
  /// </summary>
  /// <remarks>
  /// This is a bitfield of <see cref="MatchState"/> that will often be updated
  /// in the background as matches progress. As this field is very volatile, it
  /// may cause a lot of GC activity with snapshotting due to frequent polling.
  /// <para/>
  /// This field may not always be returned the first time (as references to
  /// this field often change), so retrieval is attempted multiple times or else
  /// the field is set to <see cref="MatchState.Invalid"/>.
  /// </remarks>
  public MatchState State =>
    Retry(() => Cast<MatchState>(Unbind(@base).Status), MatchState.Invalid);

  /// <summary>
  /// Whether the match has been completed.
  /// </summary>
  public bool IsComplete => Unbind(@base).IsCompleted ||
    State == (MatchState.MatchCompleted | MatchState.GameClosed);

  /// <summary>
  /// The user who created the match.
  /// </summary>
  [Default(null)]
  public User? Creator =>
    Optional<User>(@base.Creator?.Name);

  /// <summary>
  /// The user being challenged to the match.
  /// </summary>
  [Default(null)]
  public User? ChallengeReceiver =>
    Optional<User>(@base.ChallengeReceiver?.Name);

  /// <summary>
  /// The challenge text sent to the challenge receiver.
  /// </summary>
  public string ChallengeText => @base.ChallengeText;

  /// <summary>
  /// The games played in this match.
  /// </summary>
  public IList<Game> Games => Map<IList, Game>(@base.Games, proxy: true);

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
  public IList<User> WinningPlayers =>
    Map<IList, User>(Unbind(@base.WinningPlayers));

  /// <summary>
  /// The player(s) who lost the match.
  /// </summary>
  public IList<User> LosingPlayers =>
    Map<IList, User>(Unbind(@base.LosingPlayers));

  //
  // IMatch wrapper events
  //

  public EventHookWrapper<Game> OnGameStarted =
    new(GameStarted, new((s,_) => s.Id == match.MatchId));

  public EventHookWrapper<Game> OnGameEnded =
    new(GameEnded, new((s,_) => s.Id == match.MatchId));

  public EventHookWrapper<MatchState> OnMatchStateChanged =
    new(MatchStateChanged, new((s,_) => s.Id == match.MatchId));

  public EventHookWrapper<Deck> OnSideboardingDeckChanged =
    new(DeckForSideboardingChanged, new((s,_) => s.Id == match.MatchId));

  //
  // IMatch static events
  //

  public static EventHookProxy<Match, MatchState> MatchStateChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase>(),
      "OnMatchStatusChanged",
      new((instance, args) =>
      {
        MatchState state = Cast<MatchState>(args[0].NewStatus);
        if (state == MatchState.Invalid) return null; // Ignore invalid states

        Match match = new(instance);

        return (match, state); // Return a tuple of (Match, MatchState)
      })
    );

  public static EventHookProxy<Match, Game> GameStarted =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase>(),
      "OnCurrentGameChanged",
      new((instance, args) =>
      {
        if (args[0] == null) return null; // Ignore invalid game objects

        Match match = new(instance);
        Game game = new(args[0]);

        return (match, game); // Return a tuple of (Match, Game)
      })
    );

  public static EventHookProxy<Match, Game> GameEnded =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase>(),
      "OnGameEnded",
      new((instance, args) =>
      {
        if (args[0] == null) return null; // Ignore invalid game objects

        Match match = new(instance);
        Game game = new(args[0]);

        return (match, game); // Return a tuple of (Match, Game)
      })
    );

  public static EventHookProxy<Match, Deck> DeckForSideboardingChanged =
    new(
      new TypeProxy<Shiny.Play.ViewModels.SideboardingViewModel>(),
      "SubmitDeck",
      new((instance, _) =>
      {
        Match match = new(instance.m_match);
        Deck deck = new(instance.Deck);
        if (deck == null) return null; // Ignore invalid deck objects

        return (match, deck); // Return a tuple of (Match, Deck)
      })
    );
}
