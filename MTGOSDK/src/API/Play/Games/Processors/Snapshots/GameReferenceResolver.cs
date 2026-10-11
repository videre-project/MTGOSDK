/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Batching;


namespace MTGOSDK.API.Play.Games.Processors.Snapshots;

/// <summary>
/// Resolves and shares live player and zone references for one game.
/// </summary>
/// <remarks>
/// Use <see cref="CreateCard"/> to give multiple snapshots the same resolver.
/// Disposing the resolver releases its game and cached references.
/// </remarks>
public sealed class GameReferenceResolver : IDisposable
{
  // Share a reference scope between adapters constructed from the same raw game.
  // The weak association does not keep a game alive when its consumers are gone.
  private static readonly ConditionalWeakTable<object, GameReferenceResolver>
    s_shared = new();
  private static readonly object s_sharedLock = new();

  private sealed class ResolvedZone(object raw)
  {
    public object Raw { get; } = raw;
    private GameZone? _wrapper;
    public GameZone Wrapper => _wrapper ??= new GameZone(Raw);
  }

  private readonly object _lock = new();
  private readonly Dictionary<(uint ZoneId, int OwnerIndex), ResolvedZone> _zones = new();
  private readonly Dictionary<int, GamePlayer> _players = new();
  private object? _game;
  private bool _disposed;

  //
  // Reference scope
  //

  /// <summary>
  /// Creates an independent reference scope for a raw MTGO game or SDK Game.
  /// </summary>
  public GameReferenceResolver(object? game)
  {
    _game = UnwrapGame(game);
  }

  internal static GameReferenceResolver ForGame(object? game)
  {
    if (game is Game wrapper)
      return GameProcessor.ForGame(wrapper).References;
    game = UnwrapGame(game);
    if (game == null)
      return new GameReferenceResolver(null);
    lock (s_sharedLock)
      return s_shared.GetValue(game, static raw => new GameReferenceResolver(raw));
  }

  private static object? UnwrapGame(object? game)
  {
    if (game is DLRWrapper wrapper) game = DLRWrapper.Unbind(wrapper);
    return game == null
      ? null
      : (object)DLRWrapper.Unbind((dynamic)game);
  }

  internal object? RawGame
  {
    get
    {
      lock (_lock)
        return _game;
    }
  }

  internal int? LocalPlayerId
  {
    get
    {
      lock (_lock)
      {
        if (_disposed || _game == null)
          return null;
        try
        {
          dynamic game = _game;
          return (int)game.Players[game.LocalPlayerIndex].Id;
        }
        catch
        {
          return null;
        }
      }
    }
  }

  //
  // Card adapters and reference resolution
  //

  /// <summary>
  /// Creates a local card from the named capture outputs.
  /// </summary>
  public GameCard CreateCard(PropertyContainer properties, RemoteBatchObject snapshot) =>
    new(new GameCardPartial(this, properties, snapshot));

  /// <summary>
  /// Creates an unidentified display placeholder in this reference scope.
  /// </summary>
  public GameCard CreatePlaceholder(PropertyContainer properties) =>
    new(GameCardPartial.CreatePlaceholder(this, properties));

  /// <summary>
  /// Resolves a player by server-order index. Unavailable players return null
  /// and are retried on the next read.
  /// </summary>
  public GamePlayer? ResolvePlayer(int playerIndex)
  {
    lock (_lock)
    {
      if (_disposed || _game == null || playerIndex < 0)
        return null;
      if (_players.TryGetValue(playerIndex, out var cached))
        return cached;
      try
      {
        dynamic game = _game;
        object? raw = game.PlayersInServerOrder[playerIndex];
        if (raw == null)
          return null;
        var player = new GamePlayer(raw);
        _players.Add(playerIndex, player);
        return player;
      }
      catch
      {
        return null;
      }
    }
  }

  /// <summary>
  /// Resolves a shared zone first, then the owner's zone. If ownerIndex is
  /// negative, tries each player. Unavailable zones are retried on the next read.
  /// </summary>
  public GameZone? ResolveZone(uint zoneId, int ownerIndex = -1)
  {
    lock (_lock)
      return ResolveZoneEntry(zoneId, ownerIndex)?.Wrapper;
  }

  internal object? ResolveZoneObject(uint zoneId, int ownerIndex)
  {
    lock (_lock)
      return ResolveZoneEntry(zoneId, ownerIndex)?.Raw;
  }

  // Called under _lock so one native lookup and one wrapper are published
  // per successful zone resolution, including concurrent snapshot reads.
  private ResolvedZone? ResolveZoneEntry(uint zoneId, int ownerIndex)
  {
    if (_disposed || _game == null)
      return null;
    var key = (zoneId, ownerIndex);
    if (_zones.TryGetValue(key, out var cached))
      return cached;
    try
    {
      dynamic game = _game;
      dynamic? proxyCard = null;
      foreach (var thing in game.m_digitalThings.Values)
      {
        if (thing != null)
        {
          proxyCard = thing;
          break;
        }
      }
      if (proxyCard == null)
        return null;

      // Try the shared zone first, then the owner's zone. If ownerIndex is negative,
      // try each player in server order. Here we return the first non-null zone.
      object? raw = proxyCard.GetSharedCardZone(zoneId);
      if (raw == null)
      {
        if (ownerIndex >= 0)
        {
          raw = proxyCard.GetPlayerCardZone(zoneId, game.PlayersInServerOrder[ownerIndex]);
        }
        else
        {
          int count = (int)game.PlayersInServerOrder.Count;
          for (int index = 0; index < count && raw == null; index++)
            raw = proxyCard.GetPlayerCardZone(zoneId, game.PlayersInServerOrder[index]);
        }
      }
      if (raw == null)
        return null;
      var resolved = new ResolvedZone(raw);
      _zones.Add(key, resolved);
      return resolved;
    }
    catch
    {
      return null;
    }
  }

  //
  // Cache and lifetime management
  //

  /// <summary>
  /// Releases cached wrappers without ending the scope. Subsequent reads
  /// resolve references again against the same game.
  /// </summary>
  public void ClearCaches()
  {
    lock (_lock)
    {
      _zones.Clear();
      _players.Clear();
    }
  }

  // Retire the weak factory association immediately when processing ends.
  // In-flight snapshots can still use this resolver until they finish, while
  // a replacement processing scope receives a fresh resolver.
  internal void StopSharing()
  {
    lock (s_sharedLock)
    lock (_lock)
    {
      if (_game != null && s_shared.TryGetValue(_game, out var shared)
        && ReferenceEquals(shared, this))
        s_shared.Remove(_game);
    }
  }

  /// <summary>
  /// Ends the scope and releases cached references.
  /// </summary>
  public void Dispose()
  {
    // Use the same lock order as ForGame so removing a factory association
    // and creating its replacement cannot race.
    lock (s_sharedLock)
    lock (_lock)
    {
      if (_disposed)
        return;
      _disposed = true;
      if (_game != null && s_shared.TryGetValue(_game, out var shared)
          && ReferenceEquals(shared, this))
        s_shared.Remove(_game);
      _zones.Clear();
      _players.Clear();
      _game = null;
    }
  }
}
