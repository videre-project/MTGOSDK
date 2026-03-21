/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

using MTGOSDK.API.Play.Games;


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
  private readonly UserPartial _user;
  private readonly dynamic? _game;

  public int Id => _playerIndex;
  public string Name => _name;
  public dynamic User => _user;
  public dynamic m_user => _user;
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
    _user = new UserPartial(playerIndex, _name);
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
        result = _user;
        return true;
      case "m_user":
        result = _user;
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
  /// Parses all players from a PlayerStatusElement and the game instance.
  /// Returns a dictionary keyed by player index.
  /// </summary>
  public static Dictionary<int, GamePlayerPartial> ParseFromStatusElement(
    dynamic playerStatusElement,
    dynamic gameInstance)
  {
    var players = new Dictionary<int, GamePlayerPartial>();
    dynamic game = null;
    try { game = gameInstance.Game ?? gameInstance; } catch { }

    try
    {
      int activePlayer = (int)(playerStatusElement.ActivePlayer ?? 0);
      int gamePlayerCount = 0;
      try { gamePlayerCount = (int)game.Players.Count; } catch { }

      // PlayerStatusElement exposes short[16] for counts, int[8] for time
      short[] lifes = playerStatusElement.Lifes;
      short[] handCounts = playerStatusElement.HandCounts;
      short[] libraryCounts = playerStatusElement.LibraryCounts;
      short[] graveyardCounts = playerStatusElement.GraveyardCounts;
      int[] timeLeft = playerStatusElement.TimeLeft;

      // Determine how many players to parse
      int parseCount = gamePlayerCount > 0 ? gamePlayerCount : 16;
      parseCount = Math.Max(parseCount, activePlayer + 1);

      // Get player names from the game instance
      var playerNames = new string[parseCount];
      for (int i = 0; i < Math.Min(parseCount, gamePlayerCount); i++)
      {
        try
        {
          dynamic player = game.Players[i];
          playerNames[i] = (string)(player.Name ?? $"Player {i}");
        }
        catch
        {
          playerNames[i] = $"Player {i}";
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

        var chessClock = TimeSpan.FromSeconds(i < timeLeft.Length ? timeLeft[i] : 0);

        players[i] = new GamePlayerPartial(i, playerNames[i] ?? $"Player {i}", game)
        {
          Life = life,
          HandCount = handCount,
          LibraryCount = libraryCount,
          GraveyardCount = graveyardCount,
          IsActivePlayer = i == activePlayer,
          ChessClock = chessClock,
        };
      }
    }
    catch
    {
      // Swallow parsing errors
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

  public sealed class UserPartial(int id, string name) : DynamicObject
  {
    public int Id { get; } = id;
    public string Name { get; } = name;

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
      switch (binder.Name)
      {
        case "Id":
          result = Id;
          return true;
        case "Name":
          result = Name;
          return true;
        default:
          result = null;
          return true;
      }
    }
  }

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
