/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;


namespace MTGOSDK.API.Play.Games.Processors.Partials;

/// <summary>
/// A local dynamic object that backs a GameCard from a PropertyContainer
/// snapshot, without requiring a remote MTGO GameCard instance.
/// </summary>
/// <remarks>
/// This class translates GameCard/IGameCard member name accesses into
/// MagicProperty lookups against a PropertyContainer dictionary. It is
/// intended to be passed as the <c>gameCard</c> parameter to the GameCard
/// constructor, enabling local-only GameCard construction without IPC.
/// </remarks>
public class GameCardPartial(
  PropertyContainer properties,
  dynamic game,
  int? previousZoneId = null) : DynamicObject
{
  private readonly int? _previousZoneId = previousZoneId;

  /// <summary>
  /// The internal property container for this card.
  /// </summary>
  public PropertyContainer Properties => properties;
  //
  // Static mapping: GameCard member name → MagicProperty
  //

  private static readonly Dictionary<string, MagicProperty> s_memberMap = new()
  {
    // Integer properties
    ["Id"]                  = MagicProperty.THINGNUMBER,
    ["CTN"]                 = MagicProperty.CARDTEXTURE_NUMBER,
    ["SourceId"]            = MagicProperty.SRC_THING_ID,
    ["Timestamp"]           = MagicProperty.CREATION_MODTIMESTAMP,
    ["Power"]               = MagicProperty.POWER,
    ["Toughness"]           = MagicProperty.TOUGHNESS,
    ["Damage"]              = MagicProperty.DAMAGE,
    ["Loyalty"]             = MagicProperty.LOYALTY,
    ["CurrentLevel"]        = MagicProperty.CURRENT_LEVEL,
    ["CurrentChapter"]      = MagicProperty.CHAPTER_NUMBER,

    // Boolean properties
    ["IsAttacking"]         = MagicProperty.ATTACKING,
    ["IsBlocking"]          = MagicProperty.BLOCKING,
    ["IsBlocked"]           = MagicProperty.BLOCKED,
    ["IsTapped"]            = MagicProperty.TAPPED,
    ["IsFlipped"]           = MagicProperty.HAS_CARD_FLIPPED,
    ["HasSummoningSickness"]= MagicProperty.SUMMONING_SICK,
    ["IsNewlyControlled"]   = MagicProperty.SUMMONING_SICK,
    ["IsActivatedAbility"]  = MagicProperty.IS_ACTIVATED_ABILITY,
    ["IsTriggeredAbility"]  = MagicProperty.IS_TRIGGERED_ABILITY,
    ["IsDelayedTrigger"]    = MagicProperty.IS_DELAYED_TRIGGER,
    ["IsReplacementEffect"] = MagicProperty.IS_REPLACEMENT_EFFECT,
    ["IsCompanion"]         = MagicProperty.IS_COMPANION,
    ["IsEmblem"]            = MagicProperty.IS_EMBLEM,
    ["HasAutoTargets"]      = MagicProperty.AUTOTARGETED,

    // Metadata properties
    ["ManaValue"]           = MagicProperty.CONVERTED_MANA_COST,
    ["Color"]               = MagicProperty.COLOR,
    ["IsToken"]             = MagicProperty.IS_TOKEN,
    ["IsCommander"]         = MagicProperty.IS_COMMANDER,

    // Type properties
    ["IsArtifact"]          = MagicProperty.IS_ARTIFACT,
    ["IsCreature"]          = MagicProperty.IS_CREATURE,
    ["IsEnchantment"]       = MagicProperty.IS_ENCHANTMENT,
    ["IsLand"]              = MagicProperty.IS_LAND,
    ["IsPlaneswalker"]      = MagicProperty.IS_PLANESWALKER,
    ["IsInstant"]           = MagicProperty.INSTANT,
    ["IsSorcery"]           = MagicProperty.SORCERY,
    ["IsLegendary"]         = MagicProperty.IS_LEGENDARY,
    ["IsBasicLand"]         = MagicProperty.IS_BASICLAND,

    // Player references
    ["Owner"]      = MagicProperty.OWNER,
    ["Controller"] = MagicProperty.CONTROLLER,
    ["Protector"]  = MagicProperty.PROTECTOR,
    ["OtherFace"]  = MagicProperty.OTHER_FACE,
    ["IsYieldAbility"] = MagicProperty.YIELDING_PLAYERS,
  };

  //
  // Dynamic keyword mapping cache: Normalized Name -> CardAbility
  //

  private static readonly Dictionary<string, CardAbility> s_abilityMap =
    Enum.GetValues<CardAbility>()
      .Where(a => a != CardAbility.Invalid)
      .ToDictionary(
        a => NormalizeAbilityName(a.ToString()),
        a => a
      );

  private static string NormalizeAbilityName(string name)
  {
    return name.ToUpperInvariant()
      .Replace("CANT", "CANNOT")
      .Replace("_", "");
  }

  //
  // DynamicObject overrides
  //

  public override bool TryGetMember(GetMemberBinder binder, out object? result)
  {
    var name = binder.Name;

    // Look up member name in the flat map
    if (s_memberMap.TryGetValue(name, out var prop))
    {
      // Dispatch based on PropertyContainer's type categorization
      if (PropertyContainer.IntProperties.Contains(prop))
      {
        result = properties[prop] ?? 0;
        return true;
      }

      if (PropertyContainer.BoolProperties.Contains(prop))
      {
        var raw = properties[prop];
        result = raw is int v && v >= 1;
        return true;
      }

      if (PropertyContainer.PlayerProperties.Contains(prop))
      {
        result = GetPlayerWrapper(prop);
        return true;
      }
    }

    // Special-case members
    switch (name)
    {
      case "Name":
        result = (string?)(properties[MagicProperty.CARDNAME_STRING]
              ?? properties[MagicProperty.ALT_NAME_STRING])
              ?? "";
        return true;

      case "GameInterface":
        result = game;
        return true;

      case "Zone" or "ActualZone":
        result = ResolveZone();
        return true;

      case "PreviousZone":
        result = _previousZoneId.HasValue
          ? ResolveZoneById((uint)_previousZoneId.Value)
          : null;
        return true;

      case "CurrentDungeonRoom":
        int ctrlId = (properties[MagicProperty.CONTROLLER] as int?) ?? -1;
        if (ctrlId != -1)
        {
          var propId = (uint)MagicProperty.TRACKER_MARKER0 + (uint)ctrlId;
          result = properties[(MagicProperty)propId] ?? -1;
        }
        else
        {
          result = -1;
        }
        return true;

      case "Counters":
        result = ResolveCounters();
        return true;

      // Combat ordering (populated by GameProcessor cross-linking)
      case "AttackingOrders":
        result = AttackingOrders.Select(p => p.Target).ToList();
        return true;
      case "BlockingOrders":
        result = BlockingOrders.Select(p => p.Target).ToList();
        return true;

      // Complex members we intentionally omit
      case "Actions":
      case "PendingTargets":
      case "Associations":
        result = null;
        return true;

      case "Definition":
        result = new DefinitionPartial(properties, game);
        return true;

      case "Abilities":
        result = ResolveAbilities();
        return true;

      case "TargetInformation":
        result = TargetInformation;
        return true;

      default:
        // Swallow unknown members — return null/default
        result = null;
        return true;
    }
  }

  internal string TargetInformation { get; set; } = string.Empty;

  private int GetInt(MagicProperty prop, int defaultValue = 0)
  {
    var raw = properties[prop];
    return raw is int v ? v : defaultValue;
  }

  private bool GetBool(MagicProperty prop) => GetInt(prop) >= 1;

  private string GetName() =>
    (string?)(properties[MagicProperty.CARDNAME_STRING]
      ?? properties[MagicProperty.ALT_NAME_STRING])
      ?? string.Empty;

  private int GetCurrentDungeonRoomValue()
  {
    int ctrlId = (properties[MagicProperty.CONTROLLER] as int?) ?? -1;
    if (ctrlId < 0) return -1;

    var propId = (uint)MagicProperty.TRACKER_MARKER0 + (uint)ctrlId;
    return properties[(MagicProperty)propId] as int? ?? -1;
  }

  private static bool DictionariesEqual<TKey, TValue>(
    IReadOnlyDictionary<TKey, TValue> left,
    IReadOnlyDictionary<TKey, TValue> right)
    where TKey : notnull
    where TValue : IEquatable<TValue>
  {
    if (left.Count != right.Count) return false;
    foreach (var (key, value) in left)
    {
      if (!right.TryGetValue(key, out var otherValue)) return false;
      if (!value.Equals(otherValue)) return false;
    }
    return true;
  }

  internal int[] GetAttackingOrderIds() =>
    AttackingOrders
      .OrderBy(o => o.Order)
      .Select(o => o.Target?.Id ?? 0)
      .ToArray();

  internal int[] GetBlockingOrderIds() =>
    BlockingOrders
      .OrderBy(o => o.Order)
      .Select(o => o.Target?.Id ?? 0)
      .ToArray();

  /// <summary>
  /// Equality compares publicly visible GameCard state for snapshot diffing.
  /// </summary>
  public override bool Equals(object? obj) =>
    obj is GameCardPartial other && Equals(other);

  private bool Equals(GameCardPartial other)
  {
    if (ReferenceEquals(this, other)) return true;

    if (!string.Equals(GetName(), other.GetName(), StringComparison.Ordinal))
      return false;

    if (GetInt(MagicProperty.THINGNUMBER, -1) != other.GetInt(MagicProperty.THINGNUMBER, -1)) return false;
    if (GetInt(MagicProperty.CARDTEXTURE_NUMBER, -1) != other.GetInt(MagicProperty.CARDTEXTURE_NUMBER, -1)) return false;
    if (GetInt(MagicProperty.SRC_THING_ID, -1) != other.GetInt(MagicProperty.SRC_THING_ID, -1)) return false;
    if (GetInt(MagicProperty.CREATION_MODTIMESTAMP, 0) != other.GetInt(MagicProperty.CREATION_MODTIMESTAMP, 0)) return false;
    if (GetInt(MagicProperty.ZONE, -1) != other.GetInt(MagicProperty.ZONE, -1)) return false;

    if ((_previousZoneId ?? -1) != (other._previousZoneId ?? -1)) return false;

    if (GetInt(MagicProperty.POWER, 0) != other.GetInt(MagicProperty.POWER, 0)) return false;
    if (GetInt(MagicProperty.TOUGHNESS, 0) != other.GetInt(MagicProperty.TOUGHNESS, 0)) return false;
    if (GetInt(MagicProperty.DAMAGE, 0) != other.GetInt(MagicProperty.DAMAGE, 0)) return false;
    if (GetInt(MagicProperty.LOYALTY, 0) != other.GetInt(MagicProperty.LOYALTY, 0)) return false;
    if (GetInt(MagicProperty.CURRENT_LEVEL, 0) != other.GetInt(MagicProperty.CURRENT_LEVEL, 0)) return false;
    if (GetInt(MagicProperty.CHAPTER_NUMBER, 0) != other.GetInt(MagicProperty.CHAPTER_NUMBER, 0)) return false;
    if (GetCurrentDungeonRoomValue() != other.GetCurrentDungeonRoomValue()) return false;

    if (GetBool(MagicProperty.ATTACKING) != other.GetBool(MagicProperty.ATTACKING)) return false;
    if (GetBool(MagicProperty.BLOCKING) != other.GetBool(MagicProperty.BLOCKING)) return false;
    if (GetBool(MagicProperty.BLOCKED) != other.GetBool(MagicProperty.BLOCKED)) return false;
    if (GetBool(MagicProperty.TAPPED) != other.GetBool(MagicProperty.TAPPED)) return false;
    if (GetBool(MagicProperty.HAS_CARD_FLIPPED) != other.GetBool(MagicProperty.HAS_CARD_FLIPPED)) return false;
    if (GetBool(MagicProperty.SUMMONING_SICK) != other.GetBool(MagicProperty.SUMMONING_SICK)) return false;
    if (GetBool(MagicProperty.IS_ACTIVATED_ABILITY) != other.GetBool(MagicProperty.IS_ACTIVATED_ABILITY)) return false;
    if (GetBool(MagicProperty.IS_TRIGGERED_ABILITY) != other.GetBool(MagicProperty.IS_TRIGGERED_ABILITY)) return false;
    if (GetBool(MagicProperty.IS_DELAYED_TRIGGER) != other.GetBool(MagicProperty.IS_DELAYED_TRIGGER)) return false;
    if (GetBool(MagicProperty.IS_REPLACEMENT_EFFECT) != other.GetBool(MagicProperty.IS_REPLACEMENT_EFFECT)) return false;
    if (GetBool(MagicProperty.IS_COMPANION) != other.GetBool(MagicProperty.IS_COMPANION)) return false;
    if (GetBool(MagicProperty.IS_EMBLEM) != other.GetBool(MagicProperty.IS_EMBLEM)) return false;
    if (GetBool(MagicProperty.YIELDING_PLAYERS) != other.GetBool(MagicProperty.YIELDING_PLAYERS)) return false;
    if (GetBool(MagicProperty.AUTOTARGETED) != other.GetBool(MagicProperty.AUTOTARGETED)) return false;

    if (GetInt(MagicProperty.OWNER, -1) != other.GetInt(MagicProperty.OWNER, -1)) return false;
    if (GetInt(MagicProperty.CONTROLLER, -1) != other.GetInt(MagicProperty.CONTROLLER, -1)) return false;
    if (GetInt(MagicProperty.PROTECTOR, -1) != other.GetInt(MagicProperty.PROTECTOR, -1)) return false;
    if (GetInt(MagicProperty.OTHER_FACE, -1) != other.GetInt(MagicProperty.OTHER_FACE, -1)) return false;

    var abilities = ResolveAbilities().OrderBy(a => (int)a).ToArray();
    var otherAbilities = other.ResolveAbilities().OrderBy(a => (int)a).ToArray();
    if (!abilities.SequenceEqual(otherAbilities)) return false;

    var counters = ResolveCounters();
    var otherCounters = other.ResolveCounters();
    if (!DictionariesEqual(counters, otherCounters)) return false;

    if (!GetAttackingOrderIds().SequenceEqual(other.GetAttackingOrderIds())) return false;
    if (!GetBlockingOrderIds().SequenceEqual(other.GetBlockingOrderIds())) return false;

    return true;
  }

  public override int GetHashCode()
  {
    var hash = new HashCode();
    hash.Add(GetName(), StringComparer.Ordinal);

    hash.Add(GetInt(MagicProperty.THINGNUMBER, -1));
    hash.Add(GetInt(MagicProperty.CARDTEXTURE_NUMBER, -1));
    hash.Add(GetInt(MagicProperty.SRC_THING_ID, -1));
    hash.Add(GetInt(MagicProperty.CREATION_MODTIMESTAMP, 0));
    hash.Add(GetInt(MagicProperty.ZONE, -1));
    hash.Add(_previousZoneId ?? -1);

    hash.Add(GetInt(MagicProperty.POWER, 0));
    hash.Add(GetInt(MagicProperty.TOUGHNESS, 0));
    hash.Add(GetInt(MagicProperty.DAMAGE, 0));
    hash.Add(GetInt(MagicProperty.LOYALTY, 0));
    hash.Add(GetInt(MagicProperty.CURRENT_LEVEL, 0));
    hash.Add(GetInt(MagicProperty.CHAPTER_NUMBER, 0));
    hash.Add(GetCurrentDungeonRoomValue());

    hash.Add(GetBool(MagicProperty.ATTACKING));
    hash.Add(GetBool(MagicProperty.BLOCKING));
    hash.Add(GetBool(MagicProperty.BLOCKED));
    hash.Add(GetBool(MagicProperty.TAPPED));
    hash.Add(GetBool(MagicProperty.HAS_CARD_FLIPPED));
    hash.Add(GetBool(MagicProperty.SUMMONING_SICK));
    hash.Add(GetBool(MagicProperty.IS_ACTIVATED_ABILITY));
    hash.Add(GetBool(MagicProperty.IS_TRIGGERED_ABILITY));
    hash.Add(GetBool(MagicProperty.IS_DELAYED_TRIGGER));
    hash.Add(GetBool(MagicProperty.IS_REPLACEMENT_EFFECT));
    hash.Add(GetBool(MagicProperty.IS_COMPANION));
    hash.Add(GetBool(MagicProperty.IS_EMBLEM));
    hash.Add(GetBool(MagicProperty.YIELDING_PLAYERS));
    hash.Add(GetBool(MagicProperty.AUTOTARGETED));

    hash.Add(GetInt(MagicProperty.OWNER, -1));
    hash.Add(GetInt(MagicProperty.CONTROLLER, -1));
    hash.Add(GetInt(MagicProperty.PROTECTOR, -1));
    hash.Add(GetInt(MagicProperty.OTHER_FACE, -1));

    foreach (var ability in ResolveAbilities().OrderBy(a => (int)a))
      hash.Add((int)ability);

    foreach (var (counter, value) in ResolveCounters().OrderBy(kvp => (int)kvp.Key))
    {
      hash.Add((int)counter);
      hash.Add(value);
    }

    foreach (var id in GetAttackingOrderIds()) hash.Add(id);
    foreach (var id in GetBlockingOrderIds()) hash.Add(id);

    return hash.ToHashCode();
  }

  public static bool operator ==(GameCardPartial? left, GameCardPartial? right) =>
    Equals(left, right);

  public static bool operator !=(GameCardPartial? left, GameCardPartial? right) =>
    !Equals(left, right);

  //
  // Combat cross-linking (populated post-snapshot by GameProcessor)
  //

  internal List<GameCard.OrderedCombatParticipant> AttackingOrders { get; } = new();
  internal List<GameCard.OrderedCombatParticipant> BlockingOrders { get; } = new();

  //
  // Zone and player cache
  //

  // (gameId, zoneId, ownerId) → resolved zone object
  private static readonly ConcurrentDictionary<(int, uint, int), object>
    s_zoneCache = new();

  // (gameId, zoneId, ownerId) → shared GameZone wrapper
  private static readonly ConcurrentDictionary<(int, uint, int), GameZone>
    s_gameZoneCache = new();

  // (gameId, playerIndex) → shared GamePlayer wrapper
  private static readonly ConcurrentDictionary<(int, int), GamePlayer>
    s_gamePlayerCache = new();

  /// <summary>
  /// Clears all cached zones and players for a specific game ID.
  /// Call this when a game ends to free references.
  /// </summary>
  public static void ClearZoneCache(int gameId)
  {
    foreach (var key in s_zoneCache.Keys)
    {
      if (key.Item1 == gameId) s_zoneCache.TryRemove(key, out _);
    }

    foreach (var key in s_gameZoneCache.Keys)
    {
      if (key.Item1 == gameId) s_gameZoneCache.TryRemove(key, out _);
    }

    foreach (var key in s_gamePlayerCache.Keys)
    {
      if (key.Item1 == gameId) s_gamePlayerCache.TryRemove(key, out _);
    }
  }

  //
  // Zone resolution
  //

  /// <summary>
  /// Resolves the card zone from the ZONE property by delegating to MTGO's
  /// own zone resolution via an existing remote GameCard proxy.
  /// </summary>
  /// <remarks>
  /// GetSharedCardZone / GetPlayerCardZone are instance methods on MTGO's
  /// GameCard that only use <c>this.Game</c> internally. We grab any
  /// existing card from the game's digital things collection as a proxy to
  /// call these methods, avoiding hardcoded zone ID mappings in the SDK.
  /// Results are cached per (gameId, zoneId, ownerId) tuple.
  /// </remarks>
  private object? ResolveZone()
  {
    var zoneId = properties[MagicProperty.ZONE];
    if (zoneId is not int || game == null) return null;

    return ResolveZoneById((uint)(int)zoneId);
  }

  /// <summary>
  /// Gets the shared <see cref="GameZone"/> wrapper for the current zone.
  /// </summary>
  public GameZone? GetZoneWrapper()
  {
    var zoneId = properties[MagicProperty.ZONE];
    if (zoneId is not int || game == null) return null;

    return GetZoneWrapperById((uint)(int)zoneId);
  }

  /// <summary>
  /// Gets the shared <see cref="GameZone"/> wrapper for the previous zone.
  /// </summary>
  public GameZone? GetPreviousZoneWrapper() =>
    _previousZoneId.HasValue
      ? GetZoneWrapperById((uint)_previousZoneId.Value)
      : null;

  /// <summary>
  /// Gets the shared <see cref="GamePlayer"/> wrapper for the owner.
  /// </summary>
  public GamePlayer GetOwnerWrapper() =>
    GetPlayerWrapper(MagicProperty.OWNER)!;

  /// <summary>
  /// Gets the shared <see cref="GamePlayer"/> wrapper for the controller.
  /// </summary>
  public GamePlayer GetControllerWrapper() =>
    GetPlayerWrapper(MagicProperty.CONTROLLER)!;

  /// <summary>
  /// Gets the shared <see cref="GamePlayer"/> wrapper for the protector.
  /// </summary>
  public GamePlayer? GetProtectorWrapper() =>
    GetPlayerWrapper(MagicProperty.PROTECTOR);

  /// <summary>
  /// Resolves a zone by its raw uint ID, using the cache first.
  /// </summary>
  private object? ResolveZoneById(uint uid)
  {
    if (game == null) return null;

    int gameId;
    int ownerId;
    try
    {
      gameId = (int)game.Id;
      var ownerRaw = properties[MagicProperty.OWNER];
      ownerId = ownerRaw is int ? (int)ownerRaw : -1;
    }
    catch
    {
      return null;
    }

    var cacheKey = (gameId, uid, ownerId);

    if (s_zoneCache.TryGetValue(cacheKey, out var cached))
      return cached;

    try
    {
      // Find any existing remote GameCard to use as a proxy for zone lookup.
      dynamic? proxyCard = null;
      foreach (var thing in game.m_digitalThings.Values)
      {
        if (thing != null) { proxyCard = thing; break; }
      }
      if (proxyCard == null) return null;

      // Try shared zone first (Battlefield, Stack, Aside, etc.)
      var zone = proxyCard.GetSharedCardZone(uid);
      if (zone != null)
      {
        s_zoneCache.TryAdd(cacheKey, zone);
        return zone;
      }

      // Fall back to per-player zone (Hand, Library, Graveyard, etc.)
      if (ownerId >= 0)
      {
        dynamic owner = game.PlayersInServerOrder[ownerId];
        zone = proxyCard.GetPlayerCardZone(uid, owner);
        if (zone != null)
        {
          s_zoneCache.TryAdd(cacheKey, zone);
          return zone;
        }
      }

      return null;
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  /// Resolves and caches the shared <see cref="GameZone"/> wrapper by zone ID.
  /// </summary>
  private GameZone? GetZoneWrapperById(uint uid)
  {
    if (!TryGetZoneCacheKey(uid, out var cacheKey)) return null;

    if (s_gameZoneCache.TryGetValue(cacheKey, out var cachedZone))
      return cachedZone;

    var zoneObj = ResolveZoneById(uid);
    if (zoneObj == null) return null;

    return s_gameZoneCache.GetOrAdd(cacheKey, _ => new GameZone(zoneObj));
  }

  /// <summary>
  /// Resolves and caches the shared <see cref="GamePlayer"/> wrapper.
  /// </summary>
  private GamePlayer? GetPlayerWrapper(MagicProperty property)
  {
    if (!TryGetPlayerCacheKey(property, out var cacheKey)) return null;

    try
    {
      return s_gamePlayerCache.GetOrAdd(cacheKey, key =>
      {
        dynamic player = game.PlayersInServerOrder[key.Item2];
        return new GamePlayer(player);
      });
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  /// Builds the shared zone cache key used by both object and wrapper caches.
  /// </summary>
  private bool TryGetZoneCacheKey(uint uid, out (int, uint, int) cacheKey)
  {
    cacheKey = default;
    if (game == null) return false;

    try
    {
      int gameId = (int)game.Id;
      var ownerRaw = properties[MagicProperty.OWNER];
      int ownerId = ownerRaw is int ? (int)ownerRaw : -1;
      cacheKey = (gameId, uid, ownerId);
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// Builds the shared player cache key from the server-order player index.
  /// </summary>
  private bool TryGetPlayerCacheKey(
    MagicProperty property,
    out (int, int) cacheKey)
  {
    cacheKey = default;
    if (game == null) return false;

    try
    {
      int gameId = (int)game.Id;
      var raw = properties[property];
      if (raw is not int playerIndex || playerIndex < 0)
        return false;

      _ = game.PlayersInServerOrder[playerIndex];
      cacheKey = (gameId, playerIndex);
      return true;
    }
    catch
    {
      return false;
    }
  }

  //
  // Counter resolution
  //

  /// <summary>
  /// Resolves counters from the COUNTERS_LIST sub-property, mirroring
  /// MTGO's GameCard.ResolveCounters() logic.
  /// Uses PropertyNames from the sub-container to match counter names
  /// to CardCounter enum values by string, rather than relying on our
  /// local MagicProperty enum having every counter type defined.
  /// </summary>
  private Dictionary<CardCounter, int> ResolveCounters()
  {
    var counters = new Dictionary<CardCounter, int>();
    var counterList = properties.GetSubproperties(MagicProperty.COUNTERS_LIST);
    if (counterList == null) return counters;

    const string suffix = "_COUNTERS";

    foreach (var (prop, val) in counterList.AllProperties)
    {
      if (val is not int count || count <= 0) continue;

      // Get the MTGO property name (e.g. "PLUS_ONE_PLUS_ONE_COUNTERS")
      string name = counterList.GetPropertyName(prop);
      if (!name.EndsWith(suffix)) continue;

      // Strip _COUNTERS, convert SNAKE_CASE to PascalCase
      var baseName = name[..^suffix.Length];
      var pascal = string.Concat(
        baseName.Split('_')
          .Where(s => s.Length > 0)
          .Select(s => char.ToUpper(s[0]) + s[1..].ToLower())
      );

      if (Enum.TryParse<CardCounter>(pascal, ignoreCase: true, out var counter)
          && counter != CardCounter.Invalid)
      {
        counters[counter] = count;
      }
    }

    return counters;
  }

  //
  // Ability resolution
  //

  /// <summary>
  /// Resolves keyword abilities from the PropertyContainer using a dynamic
  /// name-based mapping strategy.
  /// </summary>
  private List<CardAbility> ResolveAbilities()
  {
    var abilities = new HashSet<CardAbility>();

    // 1. Resolve boolean keyword properties via dynamic name mapping
    foreach (var (prop, val) in properties.AllProperties)
    {
      if (val is not int intVal || intVal < 1) continue;

      // Get the MTGO property name (e.g. "FLYING", "FIRST_STRIKE")
      string propertyName = properties.GetPropertyName(prop);
      string normName = NormalizeAbilityName(propertyName);

      if (s_abilityMap.TryGetValue(normName, out var ability))
      {
        abilities.Add(ability);
      }
    }

    // 2. Handle known complex list-based abilities (Protection, Landwalk)
    if (properties.HasProperty(MagicProperty.PROTECTION_LIST0))
    {
      abilities.Add(CardAbility.ProtectionFrom);
    }
    if (properties.HasProperty(MagicProperty.LANDWALK_LIST0))
    {
      abilities.Add(CardAbility.Landwalk);
    }

    // 3. Handle abilities with specific integer values (optional metadata)
    // Most are already covered by the boolean check if the value > 0,
    // but some might require specific property lookups if they don't follow the pattern.

    return abilities.ToList();
  }

  //
  // Nested partials for dynamic definition proxying
  //

  private class DefinitionPartial(PropertyContainer properties, dynamic game) : DynamicObject
  {
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
      switch (binder.Name)
      {
        case "Id":
          result = properties[MagicProperty.THINGNUMBER] ?? -1;
          return true;
        case "Name":
          result = properties[MagicProperty.CARDNAME_STRING] ?? "Unknown";
          return true;
        case "Description":
          result = "";
          return true;
        case "ConvertedManaCost":
          result = properties[MagicProperty.CONVERTED_MANA_COST] ?? 0;
          return true;
        case "IsToken":
          result = (properties[MagicProperty.IS_TOKEN] ?? 0) > 0;
          return true;
        case "IsTradable":
          result = (properties[MagicProperty.UNTRADABLE] ?? 0) == 0;
          return true;
        case "Power":
          result = (properties[MagicProperty.POWER] ?? 0).ToString();
          return true;
        case "Toughness":
          result = (properties[MagicProperty.TOUGHNESS] ?? 0).ToString();
          return true;
        case "InitialLoyalty":
          result = (properties[MagicProperty.LOYALTY] ?? 0).ToString();
          return true;
        case "InitialBattleDefense":
          result = (properties[MagicProperty.DEFENSE] ?? 0).ToString();
          return true;
        case "ColorDisplayString":
          result = ((MagicColors)(properties[MagicProperty.COLOR] ?? 0)).ToString();
          return true;
        case "ManaCost":
          result = properties[MagicProperty.MANA_COST_STRING] ?? "";
          return true;
        case "RulesText":
          result = properties[MagicProperty.REAL_ORACLETEXT_STRING] ?? "";
          return true;
        case "FlavorText":
          result = properties[MagicProperty.FLAVORTEXT_STRING] ?? "";
          return true;
        case "ArtistName":
          result = properties[MagicProperty.ARTIST_NAME_STRING] ?? "";
          return true;
        case "ArtId":
          result = properties[MagicProperty.ARTID] ?? 0;
          return true;
        case "CollectorInfo":
          result = properties[MagicProperty.COLLECTOR_INFO_STRING] ?? "";
          return true;
        case "CollectorNumber":
          result = properties[MagicProperty.CARDTEXTURE_NUMBER] ?? 0;
          return true;
        case "Types":
          result = new TypesPartial(properties);
          return true;
        case "Subtypes":
          result = ParseSubtypes(properties);
          return true;
        case "CardSet":
          result = new
          {
            Name = properties[MagicProperty.CARDSETNAME_STRING]?.ToString() ?? "Unknown",
            Code = properties[MagicProperty.PRINTEDCARDSET_STRING]?.ToString() ?? "",
            Type = new { EnumValue = 0 }, // SetType.Invalid
            Age = 0,
            ReleaseDate = DateTime.MinValue,
            Cards = new List<object>()
          };
          return true;
        case "Rarity":
          result = new
          {
            Name = properties[MagicProperty.RARITY_STATUS]?.ToString() ?? "Common"
          };
          return true;
        default:
          result = null;
          return true;
      }
    }

    private static IList<string> ParseSubtypes(PropertyContainer properties)
    {
      var subtypes = new List<string>();
      var subtypeString = properties[MagicProperty.SUBTYPE_STRING0]?.ToString();
      if (!string.IsNullOrEmpty(subtypeString))
      {
        subtypes.AddRange(subtypeString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
      }
      return subtypes;
    }
  }

  private class TypesPartial(PropertyContainer properties) : DynamicObject
  {
    public override string ToString()
    {
      var types = new List<string>();
      if ((properties[MagicProperty.IS_LEGENDARY] ?? 0) > 0)
      {
        types.Add("Legendary");
      }
      if ((properties[MagicProperty.IS_BASICLAND] ?? 0) > 0)
      {
        types.Add("Basic");
      }
      if ((properties[MagicProperty.HAS_SNOW_TYPE] ?? 0) > 0)
      {
        types.Add("Snow");
      }
      if ((properties[MagicProperty.IS_ENCHANT_WORLD] ?? 0) > 0)
      {
        types.Add("World");
      }

      if ((properties[MagicProperty.IS_ARTIFACT] ?? 0) > 0)
      {
        types.Add("Artifact");
      }
      if ((properties[MagicProperty.IS_CREATURE] ?? 0) > 0)
      {
        types.Add("Creature");
      }
      if ((properties[MagicProperty.IS_ENCHANTMENT] ?? 0) > 0)
      {
        types.Add("Enchantment");
      }
      if ((properties[MagicProperty.IS_LAND] ?? 0) > 0)
      {
        types.Add("Land");
      }
      if ((properties[MagicProperty.IS_PLANESWALKER] ?? 0) > 0)
      {
        types.Add("Planeswalker");
      }
      if ((properties[MagicProperty.INSTANT] ?? 0) > 0)
      {
        types.Add("Instant");
      }
      if ((properties[MagicProperty.SORCERY] ?? 0) > 0)
      {
        types.Add("Sorcery");
      }
      if ((properties[MagicProperty.IS_BATTLE] ?? 0) > 0)
      {
        types.Add("Battle");
      }
      if ((properties[MagicProperty.IS_PLANE] ?? 0) > 0)
      {
        types.Add("Plane");
      }
      if ((properties[MagicProperty.IS_PHENOMENON] ?? 0) > 0)
      {
        types.Add("Phenomenon");
      }
      if ((properties[MagicProperty.IS_KINDRED] ?? 0) > 0)
      {
        types.Add("Kindred");
      }
      if ((properties[MagicProperty.IS_VANGUARD] ?? 0) > 0)
      {
        types.Add("Vanguard");
      }
      if ((properties[MagicProperty.IS_CONSPIRACY] ?? 0) > 0)
      {
        types.Add("Conspiracy");
      }

      return string.Join(", ", types);
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
      // ICardDefinition.Types usage in Card.cs tries to cast to IList usually,
      // but it also calls ToString() and splits it.
      // We'll let it handle the ToString() path.
      result = null;
      return false;
    }
  }
}
