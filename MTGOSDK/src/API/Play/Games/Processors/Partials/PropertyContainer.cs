/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;


namespace MTGOSDK.API.Play.Games.Processors.Partials;

public class PropertyContainer : DLRWrapper<object>
{
  internal override dynamic obj => _propertyContainer;

  private readonly dynamic _propertyContainer;

  private readonly Dictionary<MagicProperty, dynamic> _properties;

  /// <summary>
  /// Maps numeric MagicProperty IDs to their original MTGO name strings.
  /// Populated during ToString() parsing for properties not in our local enum.
  /// </summary>
  private readonly Dictionary<MagicProperty, string> _propertyNames = new();

  /// <summary>
  /// Global registry of all property names discovered across all instances.
  /// Once a name is discovered from any card's ToString(), it's available
  /// for all future lookups.
  /// </summary>
  private static readonly Dictionary<MagicProperty, string> s_globalNames = new();

  public PropertyContainer(dynamic propertyContainer)
  {
    _propertyContainer = propertyContainer;

    if (propertyContainer is string text)
    {
      _properties = GetProperties(text, _propertyNames);
    }
    else if (propertyContainer is Dictionary<MagicProperty, dynamic> dict)
    {
      _properties = dict;
    }
    else
    {
      _properties = GetProperties(
        propertyContainer?.ToString() ?? string.Empty, _propertyNames);

      // Capture any sub-properties that weren't picked up by ToString()
      // parsing, by accessing SubProperties directly on the remote object.
      CaptureSubProperties(propertyContainer);
    }
  }

  /// <summary>
  /// Captures sub-property containers (e.g. COUNTERS_LIST) directly from a
  /// remote PropertyContainer object via dynamic access. Acts as a fallback
  /// for any sub-containers that ToString() parsing may have missed.
  /// </summary>
  private void CaptureSubProperties(dynamic container)
  {
    try
    {
      dynamic subDict = Try(
        () => (object)container.SubProperties,
        () => (object)container.SubPropertiesDictionary);
      if (subDict == null) return;

      foreach (dynamic entry in (IEnumerable)subDict)
      {
        dynamic key = entry.Key;
        dynamic val = entry.Value;
        if (key == null || val == null) continue;

        var prop = (MagicProperty)(uint)(int)key;

        // Skip if already captured by ToString() parsing
        if (_properties.TryGetValue(prop, out var existing)
            && existing is PropertyContainer)
          continue;

        // Store the MTGO name for this property
        try
        {
          string enumName = key.ToString();
          if (enumName != null && !uint.TryParse(enumName, out _))
          {
            _propertyNames[prop] = enumName;
            s_globalNames[prop] = enumName;
          }
        }
        catch { }

        // Recursively snapshot the sub-container
        _properties[prop] = new PropertyContainer(val);
      }
    }
    catch
    {
      // Remote object access may fail; sub-properties are best-effort.
    }
  }

  /// <summary>
  /// Gets all properties in the container.
  /// </summary>
  public IReadOnlyDictionary<MagicProperty, dynamic> AllProperties => _properties;

  /// <summary>
  /// Maps numeric MagicProperty IDs to their original MTGO name strings.
  /// For properties defined in our local enum, returns the enum name.
  /// For unknown properties, returns the name parsed from MTGO's ToString().
  /// </summary>
  public IReadOnlyDictionary<MagicProperty, string> PropertyNames => _propertyNames;

  /// <summary>
  /// Gets the display name for a MagicProperty, preferring the MTGO name
  /// for unknown properties over the raw numeric ID.
  /// </summary>
  public string GetPropertyName(MagicProperty prop)
  {
    if (_propertyNames.TryGetValue(prop, out var name))
      return name;

    // Check the global registry (names from other cards)
    if (s_globalNames.TryGetValue(prop, out name))
      return name;

    var enumName = prop.ToString();
    return uint.TryParse(enumName, out _) ? $"{(uint)prop}" : enumName;
  }

  /// <summary>
  /// Gets the hash of the property container based on all properties.
  /// </summary>
  public int Hash =>
    _properties
      .OrderBy(kvp => kvp.Key)
      .Aggregate(0, (hash, kvp) =>
          hash ^ kvp.Key.GetHashCode() ^ kvp.Value.GetHashCode());

  /// <summary>
  /// Gets a hash considering only snapshot-relevant properties.
  /// All properties are included except those in the hash exclusion list.
  /// Zone transitions, ability changes, and counter changes all produce
  /// different hashes.
  /// </summary>
  public int SnapshotHash =>
    _properties
      .Where(kvp => !HashExcludedProperties.Contains(kvp.Key)
                  && !IsHashExcludedByName(kvp.Key))
      .OrderBy(kvp => kvp.Key)
      .Aggregate(0, (hash, kvp) =>
        hash ^ kvp.Key.GetHashCode() ^ kvp.Value.GetHashCode());

  //
  // MagicProperty type categorization
  //

  /// <summary>
  /// Properties that hold integer values (power, toughness, IDs, etc.)
  /// </summary>
  internal static readonly HashSet<MagicProperty> IntProperties =
  [
    MagicProperty.THINGNUMBER,
    MagicProperty.CARDTEXTURE_NUMBER,
    MagicProperty.SRC_THING_ID,
    MagicProperty.POWER,
    MagicProperty.TOUGHNESS,
    MagicProperty.DAMAGE,
    MagicProperty.LOYALTY,
    MagicProperty.CURRENT_LEVEL,
    MagicProperty.CHAPTER_NUMBER,
    MagicProperty.GS_SPLITCARD_ID0,
    MagicProperty.GS_SPLITCARD_ID1,
    MagicProperty.ATTACHED_TO_ID,
    MagicProperty.CREATION_MODTIMESTAMP,
    MagicProperty.SHOW_ATTACHED_TO,
    MagicProperty.MUTATE_PARENT_ID,
    MagicProperty.ENCODED_ON,
    MagicProperty.REMOVED_FROM_GAME_BY_ID,
    MagicProperty.IMPRINTED_CARD_ID0,
    MagicProperty.PAIRED_WITH_ID,
    MagicProperty.HAUNTED_THING,
    MagicProperty.DRAW_ARROW_FROM_ID,
    MagicProperty.CHOSEN_SOURCE,
  ];

  /// <summary>
  /// Properties that hold boolean values (stored as int >= 1 in MTGO).
  /// </summary>
  internal static readonly HashSet<MagicProperty> BoolProperties =
  [
    MagicProperty.ATTACKING,
    MagicProperty.BLOCKING,
    MagicProperty.BLOCKED,
    MagicProperty.TAPPED,
    MagicProperty.HAS_CARD_FLIPPED,
    MagicProperty.SUMMONING_SICK,
    MagicProperty.IS_ACTIVATED_ABILITY,
    MagicProperty.IS_TRIGGERED_ABILITY,
    MagicProperty.IS_DELAYED_TRIGGER,
    MagicProperty.IS_REPLACEMENT_EFFECT,
    MagicProperty.IS_COMPANION,
    MagicProperty.IS_EMBLEM,
    MagicProperty.IS_TOKEN,
    MagicProperty.IS_LAND,
  ];

  /// <summary>
  /// Properties that hold player indices (owner, controller, protector).
  /// </summary>
  internal static readonly HashSet<MagicProperty> PlayerProperties =
  [
    MagicProperty.OWNER,
    MagicProperty.CONTROLLER,
    MagicProperty.PROTECTOR,
  ];

  /// <summary>
  /// Properties that hold string values (card names).
  /// </summary>
  internal static readonly HashSet<MagicProperty> StringProperties =
  [
    MagicProperty.CARDNAME_STRING,
    MagicProperty.ALT_NAME_STRING,
    MagicProperty.DIGITAL_OBJECT_TYPE_CODE_STRING,
  ];

  /// <summary>
  /// Properties excluded from the snapshot hash to avoid spurious
  /// re-materialization from UI/engine-internal state changes.
  /// </summary>
  public static readonly HashSet<MagicProperty> HashExcludedProperties =
  [
    MagicProperty.YIELDING_PLAYERS,
    MagicProperty.AUTOTARGETED,
    MagicProperty.CARD_BEING_PLAYED,
  ];

  /// <summary>
  /// Properties excluded from PropertyChangeTracker log output.
  /// These are still hashed (needed for correctness) but handled
  /// by other processors or are static metadata.
  /// </summary>
  public static readonly HashSet<MagicProperty> LogExcludedProperties =
  [
    MagicProperty.ZONE,
    MagicProperty.CREATION_MODTIMESTAMP,
    MagicProperty.THINGNUMBER,
    MagicProperty.SRC_THING_ID,
    MagicProperty.SPLITPARENT_ID,
    MagicProperty.GS_SPLITCARD_ID0,
    MagicProperty.GS_SPLITCARD_ID1,
    MagicProperty.TRIGGER_SOURCE_ID,
    MagicProperty.ACTION_TRIGGERING_THING,
    MagicProperty.IS_ACTIVATED_ABILITY,
    MagicProperty.IS_TRIGGERED_ABILITY,
    MagicProperty.IS_DELAYED_TRIGGER,
    MagicProperty.IS_REPLACEMENT_EFFECT,
    MagicProperty.CARDNAME_STRING,
    MagicProperty.ALT_NAME_STRING,
    MagicProperty.YIELDING_PLAYERS,
    MagicProperty.AUTOTARGETED,
    MagicProperty.CARD_BEING_PLAYED,
  ];

  /// <summary>
  /// Name patterns excluded from the snapshot hash for unknown properties.
  /// Matches against the MTGO name (e.g. "LEVEL1_POWER", "FACE_DOWN_POWER").
  /// </summary>
  private static readonly string[] s_hashExcludedNamePatterns =
  [
    "LEVEL",         // LEVEL1_POWER, LEVEL2_TOUGHNESS, etc.
    "FACE_DOWN_",    // FACE_DOWN_POWER, FACE_DOWN_TOUGHNESS
    "ACTION_",       // ACTION_PENDING_TARGETID0, ACTION_SPELL_TEXT_STRING, etc.
    "TARGET_",       // TARGET_COMMENT_LIST0, TARGET_COMMENT_STRING0, etc.
    "IS_SPELL",      // Transient casting state
    "MANA_SPENT",    // MANA_SPENT_TO_CAST
    "NUMBER_OF_CASTING", // NUMBER_OF_CASTING_COLORS
  ];

  /// <summary>
  /// Name patterns excluded from PropertyChangeTracker log output,
  /// but still hashed to track component state changes.
  /// </summary>
  private static readonly string[] s_logExcludedNamePatterns =
  [
    "BLOCKING_",     // BLOCKING_SORT0, BLOCKING_ID0, etc. (we keep the underlying BLOCKING flag)
    "ATTACKING_",    // ATTACKING_SORT0, ATTACKING_ID0, etc.
  ];

  /// <summary>
  /// Checks if an unknown property should be excluded from the hash
  /// based on its MTGO name pattern.
  /// </summary>
  private bool IsHashExcludedByName(MagicProperty prop)
  {
    string name = null;
    _propertyNames.TryGetValue(prop, out name);
    if (name == null) s_globalNames.TryGetValue(prop, out name);
    if (name == null) name = prop.ToString();

    if (uint.TryParse(name, out _))
      return true;

    foreach (var pattern in s_hashExcludedNamePatterns)
    {
      if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        return true;
    }
    return false;
  }

  /// <summary>
  /// Checks if a property should be excluded from log output,
  /// including pattern-based exclusions for unknown properties.
  /// </summary>
  public bool IsLogExcluded(MagicProperty prop)
  {
    if (LogExcludedProperties.Contains(prop))
      return true;

    // Resolve the property's text name
    string name = null;
    _propertyNames.TryGetValue(prop, out name);
    if (name == null) s_globalNames.TryGetValue(prop, out name);
    if (name == null) name = prop.ToString();

    // Exclude properties with purely numeric names (dynamic MTGO containers)
    if (uint.TryParse(name, out _))
      return true;

    // Exclude properties matching known noisy patterns
    foreach (var pattern in s_hashExcludedNamePatterns)
    {
      if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    foreach (var pattern in s_logExcludedNamePatterns)
    {
      if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        return true;
    }
    return false;
  }

  public override bool Equals(object? obj) =>
    obj is PropertyContainer other && Hash == other.Hash;

  public override int GetHashCode() => Hash;

  private static Dictionary<MagicProperty, dynamic> GetProperties(
    string text, Dictionary<MagicProperty, string> nameMap)
  {
    var properties = new Dictionary<MagicProperty, dynamic>();
    if (string.IsNullOrWhiteSpace(text)) return properties;

    string[] lines = text.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
    int index = 0;
    ParseContainer(lines, ref index, properties, nameMap);

    return properties;
  }

  private static void ParseContainer(
    string[] lines,
    ref int index,
    Dictionary<MagicProperty, dynamic> properties,
    Dictionary<MagicProperty, string> nameMap
  )
  {
    while (index < lines.Length)
    {
      string line = lines[index].Trim();
      if (line == "PropertyContainer:") 
      {
          index++;
          break;
      }
      if (line == "]" || line == "],") return; 
      index++;
    }

    while (index < lines.Length)
    {
      string line = lines[index++];
      string trimmed = line.Trim();
      
      if (trimmed == "]" || trimmed == "],")
      {
        return;
      }

      if (!line.StartsWith("    ")) continue;

      int bracket1 = line.IndexOf('[');
      int bracket2 = line.IndexOf(']');

      if (bracket1 == -1 || bracket2 == -1) continue;
      
      int equals = line.IndexOf('=', bracket2); 
      if (equals == -1) continue;

      string nameStr = line.Substring(4, bracket1 - 4).Trim();
      string idStr = line.Substring(bracket1 + 1, bracket2 - bracket1 - 1);
      
      MagicProperty prop;
      if (!Enum.TryParse<MagicProperty>(nameStr, out prop)) 
      {
        if (uint.TryParse(idStr, out uint id))
        {
          prop = (MagicProperty)id;
          nameMap[prop] = nameStr;
          s_globalNames[prop] = nameStr;
        }
        else
          continue;
      }
      else
      {
        nameMap[prop] = nameStr;
        s_globalNames[prop] = nameStr;
      }
      
      string valueStr = line.Substring(equals + 1);

      if (string.IsNullOrWhiteSpace(valueStr))
      {
        // Skip blank lines between "=" and "[" (MTGO's ToString() emits
        // an extra newline before sub-container brackets).
        int peek = index;
        while (peek < lines.Length && string.IsNullOrWhiteSpace(lines[peek]))
          peek++;

        if (peek < lines.Length && lines[peek].Trim() == "[")
        {
          index = peek + 1; // skip blank lines and "["
          var subProps = new Dictionary<MagicProperty, dynamic>();
          var subNameMap = new Dictionary<MagicProperty, string>();
          ParseContainer(lines, ref index, subProps, subNameMap);
          var subContainer = new PropertyContainer(subProps);
          subContainer._propertyNames.Clear();
          foreach (var kvp in subNameMap)
            subContainer._propertyNames[kvp.Key] = kvp.Value;
          properties[prop] = subContainer;
        }
        else
        {
          properties[prop] = "";
        }
      }
      else
      {
        if (valueStr.EndsWith(",")) 
        {
          valueStr = valueStr.Substring(0, valueStr.Length - 1);
        }

        if (int.TryParse(valueStr, out int intVal))
        {
          properties[prop] = intVal;
        }
        else
        {
          properties[prop] = valueStr;
        }
      }
    }
  }

  public dynamic? this[MagicProperty property]
  {
    get => _properties.TryGetValue(property, out var value) ? value : null;
    set
    {
      var remoteEnum = RemoteClient.CreateEnum<WotC.MtGO.Client.Model.MagicProperty>(property.ToString());
      @base.Set(remoteEnum, value);
    }
  }

  /// <summary>
  /// Sets a property value in the local dictionary only (no remote IPC).
  /// Used by processors that need to backfill properties not present in the
  /// server's binary state (e.g. ACTION_TARGETID1..N from action messages).
  /// </summary>
  internal void SetLocal(MagicProperty property, dynamic value)
  {
    _properties[property] = value;
  }

  public PropertyContainer? GetSubproperties(MagicProperty property) =>
    _properties.TryGetValue(property, out var value) && value is PropertyContainer sub
      ? sub
      : null;

  /// <summary>
  /// Gets a sub-property container by its MTGO property name.
  /// </summary>
  internal PropertyContainer? GetSubpropertiesByName(string propertyName)
  {
    foreach (var (property, value) in _properties)
    {
      if (value is PropertyContainer subproperties &&
          string.Equals(
            GetPropertyName(property),
            propertyName,
            StringComparison.Ordinal))
        return subproperties;
    }

    return null;
  }

  /// <summary>
  /// Checks if the container has a property.
  /// </summary>
  public bool HasProperty(MagicProperty property) =>
    _properties.ContainsKey(property);
}
