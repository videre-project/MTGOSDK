/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
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

  internal Game GameInterface => new(@base.Game);

  internal IUser m_user => Bind<IUser>(Unbind(gamePlayer).m_user);

  //
  // IGamePlayer wrapper properties
  //

  public string Name => m_user.Name;

  /// <summary>
  /// The User object for the player.
  /// </summary>
  [NonSerializable]
  public User User => new(Unbind(this).User.Id);

  /// <summary>
  /// The amount of time left on the player's clock.
  /// </summary>
  public TimeSpan ChessClock =>
    Cast<TimeSpan>(Unbind(this).ChessClock);

  /// <summary>
  /// The amount of life the player has.
  /// </summary>
  public int Life => @base.Life;

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
  [NonSerializable]
  public bool IsActivePlayer => @base.IsActivePlayer;

  /// <summary>
  /// Whether the player has priority.
  /// </summary>
  [NonSerializable]
  public bool HasPriority => @base.HasPriority;

  /// <summary>
  /// The player's mana pool.
  /// </summary>
  [NonSerializable]
  public IEnumerable<Mana> ManaPool => Map<Mana>(@base.ManaPool);

  //
  // IGamePlayer wrapper methods
  //

  public override string ToString() => this.Name;

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
