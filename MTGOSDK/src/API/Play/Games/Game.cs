/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MtGO.Client.Model.Chat;
using WotC.MtGO.Client.Model.Play;

using ChannelManager = MTGOSDK.API.Chat.ChannelManager;


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
  public TimeSpan? CompletedDuration => Cast(Unbind(this).CompletedDuration);

  public IList<GameZone> SharedZones =>
    Map<IList, GameZone>(Unbind(this).m_sharedZones.Values);

  public DictionaryProxy<GamePlayer, IList<GameZone>> PlayerZones =>
    new(Unbind(this).m_playerZones,
        keyMapper: Lambda<GamePlayer>(p => new(p)),
        valueMapper: Lambda<IList<GameZone>>(z =>
            Map<IList, GameZone>(z.Values)));

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
  public void SetCardMouseOver(int cardId, bool isMouseOver = true) =>
    @base.SetCardMouseOver(cardId, isMouseOver);

  //
  // IGame wrapper events
  //

  /// <summary>
  /// Event triggered when the current game prompt changes.
  /// </summary>
  public EventHookWrapper<GamePrompt> OnPromptChanged =
    new(GamePromptChanged, new Filter<GamePrompt>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when the game completion status changes.
  /// </summary>
  public EventProxy<GameStatusEventArgs> GameStatusChanged =
    new(/* IGame */ game, "GameStateChanged");

  /// <summary>
  /// Event triggered when the player whose turn it is changes.
  /// </summary>
  public EventProxy<GameEventArgs> ActivePlayerChanged =
    new(/* IGame */ game, nameof(ActivePlayerChanged));

  /// <summary>
  /// Event triggered when the player who can take actions changes.
  /// </summary>
  public EventProxy<GameEventArgs> PriorityPlayerChanged =
    new(/* IGame */ game, nameof(PriorityPlayerChanged));

  /// <summary>
  /// Event triggered when the game results for the current game change.
  /// </summary>
  public EventHookWrapper<IList<GamePlayerResult>> OnGameResultsChanged =
    new(GameResultsChanged, new Filter<IList<GamePlayerResult>>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when the game phase for the current turn.
  /// </summary>
  public EventHookWrapper<CurrentPlayerPhase> OnGamePhaseChange =
    new(GamePhaseChanged, new Filter<CurrentPlayerPhase>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when the current turn number changes.
  /// </summary>
  public EventProxy<GameEventArgs> CurrentTurnChanged =
    new(/* IGame */ game, nameof(CurrentTurnChanged));

  /// <summary>
  /// Event triggered when any cards are added or removed from a zone.
  /// </summary>
  public EventHookWrapper<GameCard> OnZoneChange =
    new(CardZoneChanged, new Filter<GameCard>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when a game action is performed.
  /// </summary>
  public EventHookWrapper<GameAction> OnGameAction =
    new(GameActionPerformed, new Filter<GameAction>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when a player's life total changes.
  /// </summary>
  public EventHookWrapper<GamePlayer> OnLifeChange =
    new(PlayerLifeChanged, new Filter<GamePlayer>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when a log message is received.
  /// </summary>
  public EventHookWrapper<Message> OnLogMessage =
    new(LogMessageReceived, new Filter<Message>((s,_) =>
    {
      //
      // Cache the historical game log channel for this game.
      //
      // This is a performance optimization to avoid creating a new channel
      // object for each log message received.
      //
      if (!ChannelManager.s_gameLogChannels.TryGetValue(game.Id,
          out IHistoricalChatChannel channel))
      {
        channel = Bind<IHistoricalChatChannel>(game.LogChannel.HistoricalChatChannel);
        ChannelManager.s_gameLogChannels.TryAdd(game.Id, channel);
      }

      // Check if the originating channel is parented to the game log channel.
      // If so, it's a valid log message for the current game.
      return s.LocalFileName == channel.LocalFileName;
    }));

  //
  // IGame static events
  //

  /// <summary>
  /// Event triggered when the current game prompt changes in any active game.
  /// </summary>
  public static EventHookProxy<Game, GamePrompt> GamePromptChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game>(),
      "ProcessTurnStepElement",
      new((instance, args) =>
      {
        Game game = new(instance);
        DateTime __timestamp = instance.__timestamp;

        dynamic turnStep = args[0];
        GamePrompt gamePrompt = new(new
        {
          // DynamicRemoteObject properties
          __timestamp,
          // IGamePrompt properties
          Text = turnStep.PromptText,
          Timestamp = turnStep.TimeStamp,
          Options = new List<GameAction>()
        });

        return (game, gamePrompt);
      })
    );

  /// <summary>
  /// Event triggered when the current phase changes in any active game.
  /// </summary>
  public static EventHookProxy<Game, CurrentPlayerPhase> GamePhaseChanged =
    new(
      new TypeProxy<Shiny.Play.Duel.ViewModel.PhaseControllerViewModel>(),
      "set_CurrentPhase",
      new((instance, args) =>
      {
        GamePlayer activePlayer = new(args[0]);
        Game game = activePlayer.GameInterface;
        if (game == null) return null; // Ignore invalid game objects.

        GamePhase currentPhase = Cast<GamePhase>(args[1]);
        if (currentPhase == GamePhase.Invalid) return null;

        // Set timestamp on activePlayer
        Unbind(activePlayer).__timestamp = instance.__timestamp;

        return (game, new CurrentPlayerPhase(activePlayer, currentPhase));
      })
    );

  /// <summary>
  /// Event triggered when a card from any game changes zones.
  /// </summary>
  public static EventHookProxy<Game, GameCard> CardZoneChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.GameCard>(),
      "OnZoneChanged",
      new((instance, _) =>
      {
        GameCard card = new(instance);
        Game game = card.GameInterface;

        // Return a tuple of (Game, GameCard)
        return (game, card);
      })
    );

  /// <summary>
  /// Event triggered when a game action in any active game is performed.
  /// </summary>
  public static EventHookProxy<Game, GameAction> GameActionPerformed =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.Actions.GameAction>(),
      "Execute",
      new((instance, args) =>
      {
        GameAction action = GameAction.GameActionFactory(instance);
        if (action == null) return null; // Ignore unknown actions.
        if (action.IsLocal) return null; // Ignore local actions.

        Game game = new(args[0]);
        if (action.Timestamp == 0) action.SetTimestamp(game.Prompt!.Timestamp);
        if (action is CardAction cardAction) cardAction.UseTargetEvents();

        return (game, action); // Return a tuple of (Game, GameAction).
      })
    );

  /// <summary>
  /// Event triggered when a player's life total changes in any active game.
  /// </summary>
  public static EventHookProxy<Game, GamePlayer> PlayerLifeChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.GamePlayer>(),
      "OnLifeChanged",
      new((instance, _) =>
      {
        GamePlayer player = new(instance);
        if (string.IsNullOrEmpty(player.Name)) return null;
        Game game = player.GameInterface;

        // Return a tuple of (Game, GamePlayer)
        return (game, player);
      })
    );

  /// <summary>
  /// Event triggered when a game action in any active game is performed.
  /// </summary>
  public static EventHookProxy<dynamic, Message> LogMessageReceived =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Chat.HistoricalChatChannel>(),
      "AppendMessage",
      new((instance, args) =>
      {
        DateTime timestamp = args[0];
        string text = args[1];
        string username = args[2];

        Message message = new(new
        {
          Timestamp = timestamp,
          Message = text,
          FromUser = Optional<User>(instance.GetUser(username),
                                    string.IsNullOrEmpty(username))
        });

        return (instance, message);
      })
    );

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

        List<GamePlayerResult> results = new();
        foreach(var entry in message.GameResults)
        {
          int i = results.Count;
          GamePlayer player = new(instance.GetPlayerByServerIndex(i));
          PlayDrawResult playDrawResult = i == message.MovedFirst
            ? PlayDrawResult.Play
            : PlayDrawResult.Draw;

          GameResult result = Cast<GameResult>(entry.Results);
          TimeSpan clockRemaining = TimeSpan.FromSeconds(entry.PlayingTime);

          results.Add(new(player, playDrawResult, result, clockRemaining));
        }

        return (game, results); // Return a tuple of (Game, IList<GamePlayerResult>)
      })
    );
}
