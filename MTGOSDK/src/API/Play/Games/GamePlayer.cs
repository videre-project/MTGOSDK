/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;
using static MTGOSDK.API.Events;

/// <summary>
/// Represents a player in a game.
/// </summary>

[NonSerializable]
public sealed class GamePlayer(dynamic gamePlayer) : DLRWrapper<IGamePlayer>
{
  /// <summary>
  /// Stores an internal reference to the IGamePlayer object.
  /// </summary>
  internal override dynamic obj => Bind<IGamePlayer>(gamePlayer);

  //
  // IGamePlayer wrapper properties
  //

  /// <summary>
  /// The User object for the player.
  /// </summary>
  public User User => new(Unbind(@base).User.Id);

  /// <summary>
  /// The amount of time left on the player's clock.
  /// </summary>
  public TimeSpan ChessClock =>
    Cast<TimeSpan>(Unbind(@base).ChessClock);

  /// <summary>
  /// The amount of life the player has.
  /// </summary>
  public int Life => @base.Life;

  /// <summary>
  /// The number of poison counters the player has.
  /// </summary>
  public int PoisonCounters => @base.PoisonCounterQuantity;

  /// <summary>
  /// The number of energy counters the player has.
  /// </summary>
  public int EnergyCounters => @base.EnergyCounterQuantity;

  /// <summary>
  /// The number of cards in the player's graveyard.
  /// </summary>
  public int GraveyardCount => @base.GraveyardCount;

  /// <summary>
  /// The number of cards in the player's hand.
  /// </summary>
  public int HandCount => @base.HandCount;

  /// <summary>
  /// The number of cards in the player's library.
  /// </summary>
  public int LibraryCount => @base.LibraryCount;

  /// <summary>
  /// Whether the player is currently taking their turn.
  /// </summary>
  public bool IsActivePlayer => @base.IsActivePlayer;

  /// <summary>
  /// Whether the player has priority.
  /// </summary>
  public bool HasPriority => @base.HasPriority;

  /// <summary>
  /// The player's mana pool.
  /// </summary>
  public IEnumerable<Mana> ManaPool => Map<Mana>(@base.ManaPool);

  //
  // IGamePlayer wrapper methods
  //

  public override string ToString() => this.User.Name;

  //
  // IGamePlayer wrapper events
  //

  public EventProxy<GamePlayerEventArgs> IsActivePlayerChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(IsActivePlayerChanged));

  public EventProxy<GamePlayerEventArgs> GraveyardCountChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(GraveyardCountChanged));

  public EventProxy<GamePlayerEventArgs> HandCountChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(HandCountChanged));

  public EventProxy<GamePlayerEventArgs> LibraryCountChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(LibraryCountChanged));

  public EventProxy<GamePlayerEventArgs> HasPriorityChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(HasPriorityChanged));

  public EventProxy<GamePlayerEventArgs> LifeChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(LifeChanged));

  public EventProxy<GamePlayerEventArgs> StatusChanged =
    new(/* IGamePlayer */ gamePlayer, nameof(StatusChanged));
}
