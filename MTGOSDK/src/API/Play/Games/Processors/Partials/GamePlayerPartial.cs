/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Remoting.Batching;


namespace MTGOSDK.API.Play.Games.Processors.Partials;

/// <summary>
/// A local dynamic object that backs a GamePlayer from PlayerStatusElement data,
/// without requiring a remote MTGO IGamePlayer instance.
/// Proxies the same member names as IGamePlayer so GamePlayer can use it directly.
/// </summary>
public class GamePlayerPartial : DynamicObject
{
  private const int ClockTicksPerSecond = 64;

  private readonly int _playerIndex;
  private readonly string _name;
  private readonly Dictionary<string, int> _counters = new();
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

  public IReadOnlyDictionary<string, int> Counters => _counters;
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
      case "Counters":
        result = Counters;
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

    foreach (var (name, count) in source._counters)
      clone._counters[name] = count;
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
  }

  /// <summary>
  /// Builds player state from the detached status element and captured roster.
  /// Live User references are resolved once through the game's reference cache.
  /// </summary>
  internal static Dictionary<int, GamePlayerPartial> ParseFromStatusElement(
    RemoteBatchObject element, IReadOnlyList<RemoteBatchObject> identities,
    GameReferenceResolver references)
  {
    var players = new Dictionary<int, GamePlayerPartial>();
    int activePlayer = element.Get<byte>("ActivePlayer");
    var lifes = element.GetSequence<short>("Lifes");
    var handCounts = element.GetSequence<short>("HandCounts");
    var libraryCounts = element.GetSequence<short>("LibraryCounts");
    var graveyardCounts = element.GetSequence<short>("GraveyardCounts");
    var timeLeft = element.GetSequence<int>("TimeLeft");
    int parseCount = Math.Max(identities.Count > 0 ? identities.Count : 16, activePlayer + 1);

    for (int i = 0; i < parseCount; i++)
    {
      int life = i < lifes.Count ? lifes[i] : 0;
      int handCount = i < handCounts.Count ? handCounts[i] : 0;
      int libraryCount = i < libraryCounts.Count ? libraryCounts[i] : 0;
      int graveyardCount = i < graveyardCounts.Count ? graveyardCounts[i] : 0;
      if (life == 0 && handCount == 0 && libraryCount == 0 && i != activePlayer)
        continue;

      var user = i < identities.Count ? references.ResolvePlayer(i)?.User : null;
      string name = i < identities.Count
        ? identities[i].Get<string?>("Name") ?? $"Player {i}" : $"Player {i}";
      players[i] = new GamePlayerPartial(i, name, references.RawGame)
      {
        _user          = user,
        Life           = life,
        HandCount      = handCount,
        LibraryCount   = libraryCount,
        GraveyardCount = graveyardCount,
        IsActivePlayer = i == activePlayer,
        ChessClock     = TimeSpan.FromSeconds(
          (i < timeLeft.Count ? timeLeft[i] : 0) / ClockTicksPerSecond),
      };
    }
    return players;
  }

  /// <summary>
  /// Applies player state serialized outside PlayerStatusElement in the same
  /// gameplay-status message.
  /// </summary>
  internal static void ApplyMessageState(
    Dictionary<int, GamePlayerPartial> players,
    IReadOnlyDictionary<int, PropertyContainer> playerProperties,
    int priorityPlayer)
  {
    foreach (var (playerIndex, partial) in players)
    {
      partial.HasPriority = playerIndex == priorityPlayer;
      if (!playerProperties.TryGetValue(playerIndex, out var properties))
        continue;

      partial._counters.Clear();
      foreach (var (name, count) in ResolveCounters(properties))
        partial._counters[name] = count;
    }
  }

  internal static IReadOnlyDictionary<string, int> ResolveCounters(
    PropertyContainer properties)
  {
    var counters = new Dictionary<string, int>(StringComparer.Ordinal);
    var counterList = properties.GetSubpropertiesByName("COUNTERS_LIST");
    if (counterList != null)
    {
      foreach (var (property, value) in counterList.AllProperties)
      {
        var propertyName = counterList.GetPropertyName(property);
        var counterName = propertyName.EndsWith(
          "_COUNTERS", StringComparison.Ordinal)
            ? propertyName[..^"_COUNTERS".Length]
            : propertyName;
        AddCounter(counters, counterName, value);
      }
    }

    AddNamedCounter(
      counters, properties, "N_TIMES_TEMPTED", "RING_TEMPTATION");
    AddNamedCounter(counters, properties, "PLAYER_SPEED", "SPEED");
    return counters;
  }

  private static void AddNamedCounter(
    Dictionary<string, int> counters,
    PropertyContainer properties,
    string propertyName,
    string counterName)
  {
    foreach (var (property, value) in properties.AllProperties)
    {
      if (string.Equals(
            properties.GetPropertyName(property),
            propertyName,
            StringComparison.Ordinal))
      {
        AddCounter(counters, counterName, value);
        return;
      }
    }
  }

  private static void AddCounter(
    Dictionary<string, int> counters,
    string propertyName,
    object value)
  {
    try
    {
      int count = Convert.ToInt32(value);
      if (count > 0)
        counters[FormatCounterName(propertyName)] = count;
    }
    catch { }
  }

  private static string FormatCounterName(string propertyName) =>
    string.Join(' ', propertyName
      .Split('_', StringSplitOptions.RemoveEmptyEntries)
      .Select(word =>
        char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

  /// <summary>
  /// Updates a player's mana pool from a ManaPoolElement.
  /// </summary>
  internal static void UpdateManaPool(
    Dictionary<int, GamePlayerPartial> players, RemoteBatchObject element)
  {
    if (!players.TryGetValue(element.Get<int>("PlayerNumber"), out var player))
      return;

    var amounts = new Dictionary<int, int>();
    foreach (var mana in element.GetSequence<RemoteBatchObject>("Mana"))
    {
      int color = mana.Get<int>("Color");
      int amount = mana.Get<int>("Amount");
      if (amount <= 0) continue;
      amounts.TryGetValue(color, out int existing);
      amounts[color] = existing + amount;
    }

    player._manaPool.Clear();
    foreach (var (color, amount) in amounts.OrderBy(pair => pair.Key))
      player._manaPool.Add(new Mana(new ManaPartial(color, amount)));
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

    if (_counters.Count != other._counters.Count ||
        _counters.Any(counter =>
          !other._counters.TryGetValue(counter.Key, out var value) ||
          value != counter.Value))
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
    foreach (var (name, count) in _counters.OrderBy(
               counter => counter.Key, StringComparer.Ordinal))
    {
      hash.Add(name, StringComparer.Ordinal);
      hash.Add(count);
    }
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
