/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Chat;
using MTGOSDK.API.Interface.ViewModels;
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
  public DuelSceneViewModel DuelScene =>
    new(
      // TODO: Use a more efficient method of retrieving view model objects
      //       without traversing the client's managed heap.
      RemoteClient.GetInstances("Shiny.Play.Duel.ViewModel.DuelSceneViewModel")
        .FirstOrDefault(vm => Try<bool>(() => vm.GameId == this.Id))
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
  public Guid ServerGuid =>
    Cast<Guid>(Unbind(@base).ServerGuid);

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
  /// <remarks>
  /// Requires the <c>MTGOSDK.Ref.dll</c> reference assembly.
  /// </remarks>
  [Default(GamePhase.Invalid)]
  public GamePhase CurrentPhase =>
    Cast<GamePhase>(Unbind(@base).CurrentPhase);

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
  public IEnumerable<GamePlayer> Players =>
    Map<GamePlayer>(@base.Players);

  /// <summary>
  /// The game's winning players.
  /// </summary>
  public IEnumerable<GamePlayer> WinningPlayers =>
    Map<GamePlayer>(@base.WinningPlayers);

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
    @base.ExecuteAction(action.@base); // TODO: Verify method is callable.

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
    var playerKey = Unbind(player.@base);
    foreach(var zoneEntry in Unbind(@base).m_playerZones[playerKey])
    {
      // Cast enum values to avoid boxing remote key values
      if(Cast<CardZone>(zoneEntry.Key) == cardZone)
        return new GameZone(zoneEntry.Value);
    }

    throw new KeyNotFoundException($"Could not find {cardZone.ToString()}.");
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
    foreach(var zoneEntry in Unbind(@base).m_sharedZones)
    {
      // Cast enum values to avoid boxing remote key values
      if(Cast<CardZone>(zoneEntry.Key) == cardZone)
        return new GameZone(zoneEntry.Value);
    }

    throw new KeyNotFoundException($"Could not find {cardZone.ToString()}.");
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

  public EventProxy<GameEventArgs> PromptChanged =
    new(/* IGame */ game, nameof(PromptChanged));

  public EventProxy<GameEventArgs> GameChanged =
    new(/* IGame */ game, nameof(GameChanged));

  public EventProxy<GameStateEventArgs> GameStateChanged =
    new(/* IGame */ game, nameof(GameStateChanged));

  public EventProxy<GameEventArgs> ActivePlayerChanged =
    new(/* IGame */ game, nameof(ActivePlayerChanged));

  public EventProxy<GameEventArgs> PriorityPlayerChanged =
    new(/* IGame */ game, nameof(PriorityPlayerChanged));

  public EventProxy<GameEventArgs> CurrentPhaseChanged =
    new(/* IGame */ game, nameof(CurrentPhaseChanged));

  public EventProxy<GameEventArgs> CurrentTurnChanged =
    new(/* IGame */ game, nameof(CurrentTurnChanged));
}
