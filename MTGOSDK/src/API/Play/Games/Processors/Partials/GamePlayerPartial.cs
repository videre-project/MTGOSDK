/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Users;


namespace MTGOSDK.API.Play.Games.Processors.Partials;

/// <summary>
/// A local dynamic object that backs a GamePlayer from PlayerStatusElement data,
/// without requiring a remote MTGO IGamePlayer instance.
/// Proxies the same member names as IGamePlayer so GamePlayer can use it directly.
/// </summary>
public class GamePlayerPartial : DynamicObject
{
  private readonly int _playerIndex;
  private readonly string _name;
  private readonly List<Mana> _manaPool = new();
  internal User? _user;
  private readonly dynamic? _game;

  public int Id => _playerIndex;
  public string Name => _name;
  public User User => _user!;
  public dynamic m_user => User;
  public dynamic? Game => _game;

  // IGamePlayer-compatible properties
  public int Life { get; internal set; }
  public int HandCount { get; internal set; }
  public int LibraryCount { get; internal set; }
  public int GraveyardCount { get; internal set; }
  public bool IsActivePlayer { get; internal set; }
  public bool HasPriority { get; internal set; }
  public TimeSpan ChessClock { get; internal set; }

  public IReadOnlyList<Mana> ManaPool => _manaPool;

  public GamePlayerPartial(int playerIndex, string name = "", dynamic? game = null)
  {
    _playerIndex = playerIndex;
    _name = name ?? $"Player{playerIndex}";
    _game = game;
    Life = 20; // Default starting life
  }

  public override bool TryGetMember(GetMemberBinder binder, out object? result)
  {
    switch (binder.Name)
    {
      case "Id":
        result = _playerIndex;
        return true;
      case "Name":
        result = _name;
        return true;
      case "User":
        result = User;
        return true;
      case "m_user":
        result = User;
        return true;
      case "Game":
        result = _game;
        return true;
      case "Life":
        result = Life;
        return true;
      case "HandCount":
        result = HandCount;
        return true;
      case "LibraryCount":
        result = LibraryCount;
        return true;
      case "GraveyardCount":
        result = GraveyardCount;
        return true;
      case "IsActivePlayer":
        result = IsActivePlayer;
        return true;
      case "HasPriority":
        result = HasPriority;
        return true;
      case "ChessClock":
        result = ChessClock;
        return true;
      case "ManaPool":
        result = _manaPool;
        return true;
      default:
        result = null;
        return true;
    }
  }

  /// <summary>
  /// Creates a detached copy of a player partial so current-tick updates do not
  /// mutate previous snapshot state used for diffs.
  /// </summary>
  internal static GamePlayerPartial Clone(GamePlayerPartial source)
  {
    var clone = new GamePlayerPartial(source._playerIndex, source._name, source._game)
    {
      _user = source._user,
      Life = source.Life,
      HandCount = source.HandCount,
      LibraryCount = source.LibraryCount,
      GraveyardCount = source.GraveyardCount,
      IsActivePlayer = source.IsActivePlayer,
      HasPriority = source.HasPriority,
      ChessClock = source.ChessClock,
    };

    clone._manaPool.Clear();
    clone._manaPool.AddRange(source._manaPool);
    return clone;
  }

  /// <summary>
  /// Copies transient fields that may be produced by non-PlayerStatus elements
  /// (e.g., ManaPool) so they are not lost when PlayerStatus reparses players.
  /// </summary>
  internal void CopyTransientStateFrom(GamePlayerPartial source)
  {
    _manaPool.Clear();
    _manaPool.AddRange(source._manaPool);
    HasPriority = source.HasPriority;
  }

  /// <summary>
  /// Parses all players from a PlayerStatusElement and the game instance.
  /// Returns a dictionary keyed by player index.
  /// </summary>
  public static Dictionary<int, GamePlayerPartial> ParseFromStatusElement(
    dynamic playerStatusElement,
    dynamic gameInstance)
  {
    var players = new Dictionary<int, GamePlayerPartial>();
    dynamic game = gameInstance;

    try
    {
      int activePlayer = (int)(playerStatusElement.ActivePlayer ?? 0);
      int gamePlayerCount = 0;
      try { gamePlayerCount = (int)game.Players.Count; }
      catch (Exception ex)
      {
        MTGOSDK.Core.Logging.Log.Warning(ex,
          "[GamePlayerPartial] Failed to get Players.Count: {Error}", ex.Message);
      }

      // PlayerStatusElement exposes short[16] for counts, int[8] for time
      short[] lifes = playerStatusElement.Lifes;
      short[] handCounts = playerStatusElement.HandCounts;
      short[] libraryCounts = playerStatusElement.LibraryCounts;
      short[] graveyardCounts = playerStatusElement.GraveyardCounts;
      int[] timeLeft = playerStatusElement.TimeLeft;

      // Determine how many players to parse
      int parseCount = gamePlayerCount > 0 ? gamePlayerCount : 16;
      parseCount = Math.Max(parseCount, activePlayer + 1);

      // Get User objects from the game instance
      var users = new User?[parseCount];
      for (int i = 0; i < Math.Min(parseCount, gamePlayerCount); i++)
      {
        try
        {
          dynamic player = game.Players[i];
          users[i] = new User(player.User);
        }
        catch (Exception ex)
        {
          MTGOSDK.Core.Logging.Log.Warning(ex,
            "[GamePlayerPartial] Failed to get player {Index} info: {Error}",
            i, ex.Message);
        }
      }

      // Build player partials for relevant player indices
      for (int i = 0; i < parseCount; i++)
      {
        int life = i < lifes.Length ? lifes[i] : 0;
        int handCount = i < handCounts.Length ? handCounts[i] : 0;
        int libraryCount = i < libraryCounts.Length ? libraryCounts[i] : 0;
        int graveyardCount = i < graveyardCounts.Length ? graveyardCounts[i] : 0;

        // Only include players with non-zero data or the active player
        if (life == 0 && handCount == 0 && libraryCount == 0 && i != activePlayer)
          continue;

        var user = i < users.Length ? users[i] : null;
        var chessClock = TimeSpan.FromSeconds(i < timeLeft.Length ? timeLeft[i] : 0);

        players[i] = new GamePlayerPartial(i, user?.Name ?? $"Player {i}", game)
        {
          _user = user,
          Life = life,
          HandCount = handCount,
          LibraryCount = libraryCount,
          GraveyardCount = graveyardCount,
          IsActivePlayer = i == activePlayer,
          ChessClock = chessClock,
        };
      }
    }
    catch (Exception ex)
    {
      MTGOSDK.Core.Logging.Log.Warning(ex,
        "[GamePlayerPartial] Failed to parse player status element");
    }

    return players;
  }

  /// <summary>
  /// Updates a player's mana pool from a ManaPoolElement.
  /// </summary>
  public static void UpdateManaPool(
    Dictionary<int, GamePlayerPartial> players,
    dynamic manaPoolElement)
  {
    try
    {
      int playerNumber = (int)manaPoolElement.PlayerNumber;
      if (!players.TryGetValue(playerNumber, out var player)) return;

      var manaPoolByColor = new Dictionary<int, int>();
      try
      {
        dynamic manaList = manaPoolElement.Mana;
        int count = (int)manaList.Count;
        for (int i = 0; i < count; i++)
        {
          dynamic mana = manaList[i];
          int color = (int)mana.Color;
          int amount = (int)mana.Amount;

          if (amount <= 0) continue;

          manaPoolByColor.TryGetValue(color, out int existing);
          manaPoolByColor[color] = existing + amount;
        }
      }
      catch { }

      player._manaPool.Clear();
      foreach (var (color, amount) in manaPoolByColor.OrderBy(kvp => kvp.Key))
      {
        if (amount <= 0) continue;
        var manaPartial = new ManaPartial(color, amount);
        player._manaPool.Add(new Mana(manaPartial));
      }
    }
    catch
    {
      // Swallow parsing errors
    }
  }

  public override bool Equals(object? obj) =>
    obj is GamePlayerPartial other && Equals(other);

  private bool Equals(GamePlayerPartial other)
  {
    if (ReferenceEquals(this, other)) return true;

    if (_playerIndex != other._playerIndex
        || Life != other.Life
        || HandCount != other.HandCount
        || LibraryCount != other.LibraryCount
        || GraveyardCount != other.GraveyardCount
        || IsActivePlayer != other.IsActivePlayer
        || HasPriority != other.HasPriority
        || ChessClock != other.ChessClock
        || !string.Equals(_name, other._name, StringComparison.Ordinal))
      return false;

    // Compare mana pools
    if (_manaPool.Count != other._manaPool.Count) return false;
    for (int i = 0; i < _manaPool.Count; i++)
    {
      var left = _manaPool[i];
      var right = other._manaPool[i];
      if (left.ID != right.ID || left.Amount != right.Amount)
        return false;
    }

    return true;
  }

  public override int GetHashCode()
  {
    var hash = new HashCode();
    hash.Add(_playerIndex);
    hash.Add(Life);
    hash.Add(HandCount);
    hash.Add(LibraryCount);
    hash.Add(GraveyardCount);
    hash.Add(IsActivePlayer);
    hash.Add(HasPriority);
    hash.Add(ChessClock);
    hash.Add(_name, StringComparer.Ordinal);
    foreach (var item in _manaPool)
    {
      hash.Add(item.ID);
      hash.Add(item.Amount);
    }
    return hash.ToHashCode();
  }

  public static bool operator ==(GamePlayerPartial? left, GamePlayerPartial? right) =>
    Equals(left, right);

  public static bool operator !=(GamePlayerPartial? left, GamePlayerPartial? right) =>
    !Equals(left, right);

  public sealed class ManaPartial(int color, int amount) : DynamicObject
  {
    public int ID { get; } = color;
    public int Color { get; } = color;
    public int Amount { get; } = amount;

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
      switch (binder.Name)
      {
        case "ID":
          result = ID;
          return true;
        case "Color":
          result = Color;
          return true;
        case "Amount":
          result = Amount;
          return true;
        default:
          result = null;
          return true;
      }
    }
  }
}
