/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;

/// <summary>
/// Represents a player in a game.
/// </summary>
public sealed class GamePlayer(dynamic gamePlayer) : DLRWrapper<IGamePlayer>
{
  /// <summary>
  /// Stores an internal reference to the IGamePlayer object.
  /// </summary>
  internal override dynamic obj => Bind<IGamePlayer>(gamePlayer);

  /// <summary>
  /// Represents an item in the player's mana pool.
  /// </summary>
  public struct Mana
  {
    /// <summary>
    /// The unique identifier for the given mana type.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// The type of color(s) the mana represents.
    /// </summary>
    public MagicColors Color { get; init; }

    /// <summary>
    /// The amount of the given mana type in the player's mana pool.
    /// </summary>
    public int Amount { get; init; }

    public Mana(IManaPoolItem manaItem)
    {
      Id = manaItem.ID;
      Color = Cast<MagicColors>(Unbind(manaItem).Color);
      Amount = manaItem.Amount;
    }
  }

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
}
