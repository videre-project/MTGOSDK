/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;
using static MTGOSDK.API.Events;

public sealed class Game(dynamic game) : DLRWrapper<IGame>
{
  /// <summary>
  /// Stores an internal reference to the IGame object.
  /// </summary>
  internal override dynamic obj => Bind<IGame>(game);

  /// <summary>
  /// The DuelSceneViewModel of the game.
  /// </summary>
  /// <remarks>
  /// This is an instance of the game's view model, which is used by the client
  /// to control the client-side management of game state and UI elements.
  /// </remarks>
  public DuelSceneViewModel? DuelScene =>
    field ??= Optional<DuelSceneViewModel>(
      // TODO: Use a more efficient method of retrieving view model objects
      //       without traversing the client's managed heap.
      RemoteClient.GetInstances(new TypeProxy<DuelSceneViewModel>())
        .FirstOrDefault(vm => Try(() => vm.GameId == this.Id))
    );

  //
  // IGame wrapper properties
  //

  /// <summary>
  /// The unique ID for this game.
  /// </summary>
  public int Id => @base.Id;

  /// <summary>
  /// The unique game server token.
  /// </summary>
  public Guid ServerGuid => Cast<Guid>(Unbind(this).ServerGuid);

  /// <summary>
  /// The game's completion status (e.g. NotStarted, Started, Finished, etc.).
  /// </summary>
  public GameStatus Status => Cast<GameStatus>(Unbind(this).GameState);

  /// <summary>
  /// Whether the current game is a replay of a previous game.
  /// </summary>
  public bool IsReplay => @base.IsReplay;

  /// <summary>
  /// The chat channel between all players.
  /// </summary>
  [NonSerializable]
  public Channel ChatChannel => new(@base.ChatChannel);

  /// <summary>
  /// The log channel for all game actions.
  /// </summary>
  [NonSerializable]
  public Channel LogChannel => new(@base.LogChannel);

  /// <summary>
  /// The match this game is a part of.
  /// </summary>
  [NonSerializable]
  public Match Match => new(@base.Match);

  /// <summary>
  /// The current turn number.
  /// </summary>
  public int CurrentTurn => @base.CurrentTurn;

  /// <summary>
  /// The game phase of the current turn (e.g. Untap, Upkeep, Draw, etc.).
  /// </summary>
  public GamePhase CurrentPhase =>
    Cast<GamePhase>(Unbind(this).CurrentPhase);

  /// <summary>
  /// Whether the game is in the pre-game phase.
  /// </summary>
  /// <remarks>
  /// The pre-game phase is the period before the game starts, where players
  /// can mulligan, choose starting hands, etc.
  /// </remarks>
  public bool IsPreGame => Try<bool>(() => CurrentPhase >= GamePhase.PreGame1);

  /// <summary>
  /// The player whose turn it is.
  /// </summary>
  public GamePlayer? ActivePlayer =>
    Optional<GamePlayer>(@base.ActivePlayer);

  /// <summary>
  /// The player who has priority.
  /// </summary>
  public GamePlayer? PriorityPlayer =>
    Optional<GamePlayer>(@base.PriorityPlayer);

  /// <summary>
  /// The current prompt (e.g. "Choose a card to discard", etc.).
  /// </summary>
  public GamePrompt? Prompt =>
    Optional<GamePrompt>(@base.Prompt);

  /// <summary>
  /// The game's players.
  /// </summary>
  public IList<GamePlayer> Players =>
    Map<IList, GamePlayer>(@base.Players);

  /// <summary>
  /// The game's winning players.
  /// </summary>
  public IList<GamePlayer> WinningPlayers =>
    Map<IList, GamePlayer>(@base.WinningPlayers);

  /// <summary>
  /// The start time of the game.
  /// </summary>
  public DateTime StartTime => @base.StartTime;

  /// <summary>
  /// The end time of the game.
  /// </summary>
  [Default(null)]
  public DateTime? EndTime => @base.EndTime;

  /// <summary>
  /// The total duration of the game.
  /// </summary>
  [Default(null)]
  public TimeSpan? CompletedDuration => Cast<TimeSpan>(Unbind(this).CompletedDuration);

  public IList<GameZone> SharedZones =>
    Map<IList, GameZone>(Unbind(this).m_sharedZones.Values);

  public DictionaryProxy<GamePlayer, IList<GameZone>> PlayerZones =>
    new(Unbind(this).m_playerZones,
        keyMapper: Lambda<GamePlayer>(p => new(p)),
        valueMapper: Lambda<IList<GameZone>>(z =>
            Map<IList, GameZone>(z.Values)));

  private dynamic m_thingIDHistory => Unbind(this).m_thingIDHistory;

  /// <summary>
  /// Finds the original root ID for a given card ID by traversing its history backwards.
  /// </summary>
  /// <param name="currentId">The current card ID.</param>
  /// <returns>The original card ID, or the current ID if no history exists.</returns>
  public int GetOriginalCardId(int currentId)
  {
    int previousId = currentId;
    try
    {
      bool found;
      do
      {
        found = false;
        foreach (var kvp in m_thingIDHistory)
        {
          if ((int)kvp.Value == previousId)
          {
            previousId = (int)kvp.Key;
            found = true;
            break;
          }
        }
      } while (found);
    }
    catch { /* Ignore unbind or collection modification exceptions */ }

    return previousId;
  }

  /// <summary>
  /// Finds the next active ID for a given historical card ID by traversing its history forward.
  /// </summary>
  /// <param name="currentId">The historical card ID.</param>
  /// <returns>The active card ID, or the current ID if it is the latest.</returns>
  public int GetNextCardId(int currentId) =>
    Unbind(this).LookupCurrentID(currentId);

  //
  // IGame wrapper methods
  //

  // private void TwitchLogging();

  /// <summary>
  /// Gets a game card by the given card ID.
  /// </summary>
  /// <param name="cardId">The card ID to retrieve.</param>
  /// <returns>A new GameCard instance.</returns>
  public GameCard? GetGameCard(int cardId) =>
    Optional<GameCard>(Unbind(this).FindGameCard(cardId));

  // public GameCard GetGameCard(int thingNumber) =>
  //   new(@base.ResolveDigitalThingAsGameCard(thingNumber));

  /// <summary>
  /// Gets a game zone by the given player and cardzone type.
  /// </summary>
  /// <param name="player">The player to query zones from.</param>
  /// <param name="cardZone">The cardzone type to retrieve.</param>
  /// <returns>A new GameZone instance.</returns>
  /// <exception cref="KeyNotFoundException">
  /// If the given cardzone type could not be found.
  /// </exception>
  public GameZone GetGameZone(GamePlayer player, CardZone cardZone)
  {
    var key = RemoteClient.CreateEnum<WotC.MtGO.Client.Model.Play.CardZone>(
      Enum.GetName(typeof(CardZone), cardZone));

    var playerKey = Unbind(player);
    var zoneEntry = Unbind(this).m_playerZones[playerKey][key];
    if (zoneEntry != null)
      return new GameZone(zoneEntry);

    throw new KeyNotFoundException($"Could not find {cardZone}.");
  }

  /// <summary>
  /// Gets a shared game zone by the given cardzone type.
  /// </summary>
  /// <param name="cardZone">The cardzone type to retrieve.</param>
  /// <returns>A new GameZone instance.</returns>
  /// <exception cref="KeyNotFoundException">
  /// If the given cardzone type could not be found.
  /// </exception>
  public GameZone GetGameZone(CardZone cardZone)
  {
    var key = RemoteClient.CreateEnum<WotC.MtGO.Client.Model.Play.CardZone>(
      Enum.GetName(typeof(CardZone), cardZone));

    var zoneEntry = Unbind(this).m_sharedZones[key];
    if (zoneEntry != null)
      return new GameZone(zoneEntry);

    throw new KeyNotFoundException($"Could not find {cardZone}.");
  }

  /// <summary>
  /// Sets or unsets temporary focus on a given card.
  /// </summary>
  /// <param name="cardId">The card ID to update focus.</param>
  /// <param name="isFocused">Whether to highlight the card.</param>
  public void SetCardMouseOver(int cardId, bool isFocused = true) =>
    @base.SetCardMouseOver(cardId, isFocused);

  /// <inheritdoc />
  public override void ClearEvents()
  {
    try { base.ClearEvents(); }
    finally { GameProcessor.ClearEvents(this); }
  }

  //
  // Processor events
  //

  /// <summary>
  /// Event triggered when cards move between zones.
  /// </summary>
  public ProcessorEvent<ZoneChangeEventArgs> OnZoneChanged
  {
    get => field ??= GameProcessor.ZoneChanged(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when a card's properties change between snapshots.
  /// </summary>
  public ProcessorEvent<CardChangedEventArgs> OnCardChanged
  {
    get => field ??= GameProcessor.CardChanged(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when a player's properties change between snapshots.
  /// </summary>
  public ProcessorEvent<PlayerChangedEventArgs> OnPlayerChanged
  {
    get => field ??= GameProcessor.PlayerChanged(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when a game action is finalized.
  /// </summary>
  public ProcessorEvent<ActionFinalizedEventArgs> OnActionFinalized
  {
    get => field ??= GameProcessor.ActionFinalized(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when the current game prompt changes.
  /// </summary>
  public ProcessorEvent<PromptChangedEventArgs> OnPromptChanged
  {
    get => field ??= GameProcessor.PromptChanged(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when correlated gameplay or captured local interaction state changes.
  /// </summary>
  public ProcessorEvent<GameStateChangedEventArgs> OnStateChanged
  {
    get => field ??= GameProcessor.StateChanged(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when a log message is correlated with a game state snapshot.
  /// </summary>
  public ProcessorEvent<LogMessageCorrelatedEventArgs> OnLogMessage
  {
    get => field ??= GameProcessor.LogMessage(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when combat damage assignments are received from the server.
  /// </summary>
  public ProcessorEvent<DamageAssignmentEventArgs> OnDamageAssignment
  {
    get => field ??= GameProcessor.DamageAssignment(this);
    set => field = value;
  }

  /// <summary>
  /// Event triggered when cards enter or leave a revealed or pile zone.
  /// </summary>
  public ProcessorEvent<RevealedCardsEventArgs> OnRevealedCards
  {
    get => field ??= GameProcessor.RevealedCards(this);
    set => field = value;
  }

  //
  // IGame wrapper events
  //

  /// <summary>
  /// Event triggered when the game completion status changes.
  /// </summary>
  public EventProxy<GameStatusEventArgs> GameStatusChanged =
    new(/* IGame */ game, "GameStateChanged");

  /// <summary>
  /// Event triggered when the game results for the current game change.
  /// </summary>
  public EventHookWrapper<IList<GamePlayerResult>> OnGameResultsChanged =
    new(GameResultsChanged, new Filter<IList<GamePlayerResult>>((s,_) => s.Id == game.Id));

  //
  // IGame static events
  //

  /// <summary>
  /// Event triggered when the game results for the current game update.
  /// </summary>
  public static EventHookProxy<Game, IList<GamePlayerResult>> GameResultsChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game>(),
      "CompileWinningPlayers",
      new((instance, args) =>
      {
        Game game = new(instance);
        if (game == null) return null; // Ignore invalid game objects.
        if (game.IsReplay) return null; // Ignore replay games.

        dynamic message = args[1];
        if (message == null) return null; // Ignore invalid messages.

        // Always compute elapsed game time for the clock field.
        // GameResultsInfo_t.PlayingTime is unreliable -- it reports
        // sentinel values or garbage for untimed matches, and for
        // timed matches it represents time remaining and not elapsed.
        TimeSpan elapsed = game.CompletedDuration
          ?? (game.EndTime is DateTime endTime && endTime > game.StartTime
              ? endTime - game.StartTime
              : DateTime.Now - game.StartTime);

        List<GamePlayerResult> results = new();
        foreach(var entry in message.GameResults)
        {
          int i = results.Count;
          GamePlayer player = new(instance.GetPlayerByServerIndex(i));
          PlayDrawResult playDrawResult = i == message.MovedFirst
            ? PlayDrawResult.Play
            : PlayDrawResult.Draw;

          GameResult result = Cast<GameResult>(entry.Results);

          results.Add(new(player, playDrawResult, result, elapsed));
        }

        return (game, results); // Return a tuple of (Game, IList<GamePlayerResult>)
      })
    );
}
