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
  /// <para/>
  /// Note that the client's managed heap must be searched each time this
  /// property is accessed. It is best to cache this property to avoid frequent
  /// heap searches.
  /// </remarks>
  public DuelSceneViewModel? DuelScene =>
    Optional<DuelSceneViewModel>(
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
  [Default(-1)]
  public int Id => @base.Id;

  /// <summary>
  /// The unique game server token.
  /// </summary>
  public Guid ServerGuid => Cast<Guid>(Unbind(@base).ServerGuid);

  /// <summary>
  /// The game's completion status (e.g. NotStarted, Started, Finished, etc.).
  /// </summary>
  public GameStatus Status => Cast<GameStatus>(Unbind(@base).GameState);

  /// <summary>
  /// Whether the current game is a replay of a previous game.
  /// </summary>
  public bool IsReplay => @base.IsReplay;

  /// <summary>
  /// The chat channel between all players.
  /// </summary>
  public Channel ChatChannel => new(@base.ChatChannel);

  /// <summary>
  /// The log channel for all game actions.
  /// </summary>
  public Channel LogChannel => new(@base.LogChannel);

  /// <summary>
  /// The current turn number.
  /// </summary>
  public int CurrentTurn => @base.CurrentTurn;

  /// <summary>
  /// The game phase of the current turn (e.g. Untap, Upkeep, Draw, etc.).
  /// </summary>
  public GamePhase CurrentPhase => Cast<GamePhase>(Unbind(@base).CurrentPhase);

  /// <summary>
  /// The player whose turn it is.
  /// </summary>
  public GamePlayer ActivePlayer => new(@base.ActivePlayer);

  /// <summary>
  /// The player who has priority.
  /// </summary>
  public GamePlayer PriorityPlayer => new(@base.PriorityPlayer);

  /// <summary>
  /// The current prompt (e.g. "Choose a card to discard", etc.).
  /// </summary>
  public GamePrompt Prompt => new(@base.Prompt);

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
  public TimeSpan? CompletedDuration =>
    Cast<TimeSpan>(Unbind(@base).CompletedDuration);

  //
  // IGame wrapper methods
  //

  // private void TwitchLogging();

  /// <summary>
  /// Executes a game action from the current game.
  /// </summary>
  /// <param name="action">The game action to execute.</param>
  public void ExecuteAction(GameAction action) =>
    Unbind(@base).ExecuteAction(Unbind(action));

  /// <summary>
  /// Gets a game card by the given card ID.
  /// </summary>
  /// <param name="cardId">The card ID to retrieve.</param>
  /// <returns>A new GameCard instance.</returns>
  public GameCard GetGameCard(int cardId) =>
    new(Unbind(@base).FindGameCard(cardId));

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
    var key = RemoteClient.CreateEnum(
      "WotC.MtGO.Client.Model.Play.CardZone",
      Enum.GetName(typeof(CardZone), cardZone));

    var playerKey = Unbind(player);
    var zoneEntry = Unbind(@base).m_playerZones[playerKey][key];
    if (zoneEntry != null)
      return new GameZone(zoneEntry);

    // foreach(var zoneEntry in Unbind(@base).m_playerZones[playerKey])
    // {
    //   // Cast enum values to avoid boxing remote key values
    //   if (Cast<CardZone>(zoneEntry.Key) == cardZone)
    //     return new GameZone(zoneEntry.Value);
    // }

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
    var key = RemoteClient.CreateEnum(
      "WotC.MtGO.Client.Model.Play.CardZone",
      Enum.GetName(typeof(CardZone), cardZone));

    var zoneEntry = Unbind(@base).m_sharedZones[key];
    if (zoneEntry != null)
      return new GameZone(zoneEntry);

    // foreach(var zoneEntry in Unbind(@base).m_sharedZones)
    // {
    //   // Cast enum values to avoid boxing remote key values
    //   if (Cast<CardZone>(zoneEntry.Key) == cardZone)
    //     return new GameZone(zoneEntry.Value);
    // }

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
  public EventProxy<GameEventArgs> PromptChanged =
    new(/* IGame */ game, nameof(PromptChanged));

  /// <summary>
  /// Event triggered when the current game state changes.
  /// </summary>
  public EventProxy<GameEventArgs> GameChanged =
    new(/* IGame */ game, nameof(GameChanged));

  /// <summary>
  /// Event triggered when the game completion status changes.
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
  /// Event triggered when the game phase for the current turn.
  /// </summary>
  public EventProxy<GameEventArgs> CurrentPhaseChanged =
    new(/* IGame */ game, nameof(CurrentPhaseChanged));

  /// <summary>
  /// Event triggered when the current turn number changes.
  /// </summary>
  public EventProxy<GameEventArgs> CurrentTurnChanged =
    new(/* IGame */ game, nameof(CurrentTurnChanged));

  /// <summary>
  /// Event triggered when a game action is performed.
  /// </summary>
  public EventHookWrapper<GameAction> OnGameAction =
    new(GameActionPerformed, new Filter<GameAction>((s,_) => s.Id == game.Id));

  /// <summary>
  /// Event triggered when a log message is received.
  /// </summary>
  public EventHookWrapper<Message> OnLogMessage =
    new(LogMessageReceived, new Filter<Message>((s,_) => s.LocalFileName == game.LogChannel.HistoricalChatChannel.LocalFileName));

  //
  // IGame static events
  //

  /// <summary>
  /// Event triggered when a game action in any active game is performed.
  /// </summary>
  public static EventHookProxy<Game, GameAction> GameActionPerformed =
    new(
      "WotC.MtGO.Client.Model.Play.Actions.GameAction",
      "Execute",
      new EventHook((dynamic instance, dynamic[] args) =>
      {
        GameAction action = GameAction.GameActionFactory(instance);
        if (action == null) return null; // Ignore unknown actions.
        Game game = new(args[0]);

        return (game, action); // Return a tuple of (Game, GameAction).
      })
    );

  /// <summary>
  /// Event triggered when a game action in any active game is performed.
  /// </summary>
  public static EventHookProxy<dynamic, Message> LogMessageReceived =
    new(
      "WotC.MtGO.Client.Model.Chat.HistoricalChatChannel",
      "AppendMessage",
      new EventHook((dynamic instance, dynamic[] args) =>
      {
        Message message = new(new
        {
          Timestamp = args[0],
          Message = args[1],
          FromUser = new User(instance.GetUser(args[2])),
        });

        return (instance, message);
      })
    );
}
