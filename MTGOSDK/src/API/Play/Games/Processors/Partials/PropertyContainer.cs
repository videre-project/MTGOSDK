/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.API.Play.Games.Processors.Partials;

/// <summary>
/// Retains captured property values and native names, with optional native write access.
/// </summary>
public class PropertyContainer : DLRWrapper<object>
{
  internal override dynamic obj => _propertyContainer;

  private readonly dynamic _propertyContainer;

  private readonly Dictionary<MagicProperty, dynamic> _properties;

  /// <summary>
  /// Maps numeric MagicProperty IDs to their original MTGO name strings.
  /// Captured with native dictionary keys, including properties outside our local enum.
  /// </summary>
  private readonly Dictionary<MagicProperty, string> _propertyNames;

  /// <summary>
  /// Creates a local container from structured properties.
  /// </summary>
  public PropertyContainer(
    Dictionary<MagicProperty, dynamic> properties,
    IReadOnlyDictionary<MagicProperty, string>? propertyNames = null)
  {
    _propertyContainer = properties;
    _properties = properties ?? throw new ArgumentNullException(nameof(properties));
    _propertyNames = propertyNames == null ? new() : new(propertyNames);
  }

  /// <summary>
  /// Captures a native property container in one reflection batch.
  /// </summary>
  public PropertyContainer(dynamic propertyContainer)
  {
    _propertyContainer = propertyContainer;
    var snapshot = GameCardCapture.CaptureProperties((object)propertyContainer);
    _properties = snapshot._properties;
    _propertyNames = snapshot._propertyNames;
  }

  /// <summary>
  /// Decodes native property dictionaries without parsing display text.
  /// Numeric keys and native names are retained even when our enum is older.
  /// </summary>
  internal static PropertyContainer FromBatchValue(RemoteBatchValue value)
  {
    if (value.Kind != RemoteBatchValueKind.Object)
      throw new InvalidDataException("Expected a structured property container.");

    var properties = new Dictionary<MagicProperty, dynamic>();
    var names = new Dictionary<MagicProperty, string>();
    ReadDictionary("integers", static entry => entry.GetScalar<int>());
    ReadDictionary("strings", static entry => entry.Kind == RemoteBatchValueKind.Null
      ? null : entry.GetScalar<string>());
    ReadDictionary("children", static entry => FromBatchValue(entry));
    return new PropertyContainer(properties, names);

    void ReadDictionary(string name, Func<RemoteBatchValue, object?> decode)
    {
      if (!value.Members.TryGetValue(name, out var dictionary) ||
          dictionary.Kind != RemoteBatchValueKind.Dictionary)
        throw new InvalidDataException($"Missing property dictionary '{name}'.");

      foreach (var entry in dictionary.Entries)
      {
        if (entry.Key.Kind != RemoteBatchValueKind.Enum)
          throw new InvalidDataException($"Expected native enum keys in '{name}'.");
        var property = (MagicProperty)entry.Key.GetScalar<uint>();
        if (!properties.TryAdd(property, decode(entry.Value)!))
          throw new InvalidDataException($"Duplicate native property ID {(uint)property}.");
        if (entry.Key.EnumName is { } nativeName)
          names[property] = nativeName;
      }
    }
  }

  /// <summary>
  /// Gets all properties in the container.
  /// </summary>
  public IReadOnlyDictionary<MagicProperty, dynamic> AllProperties => _properties;

  /// <summary>
  /// Captured native names for numeric property IDs, including IDs unknown to
  /// the local enum. Unnamed native enum values have no entry in this map.
  /// </summary>
  public IReadOnlyDictionary<MagicProperty, string> PropertyNames => _propertyNames;

  /// <summary>
  /// Gets the captured native name, falling back to the local enum name or ID.
  /// </summary>
  public string GetPropertyName(MagicProperty prop)
  {
    if (_propertyNames.TryGetValue(prop, out var name))
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
          hash ^ kvp.Key.GetHashCode() ^ (kvp.Value?.GetHashCode() ?? 0));

  //
  // Snapshot capture exclusions
  //

  /// <summary>
  /// Properties excluded by GameProcessor's native snapshot hash so transient
  /// client state does not force card capture.
  /// </summary>
  public static readonly HashSet<MagicProperty> HashExcludedProperties =
  [
    MagicProperty.YIELDING_PLAYERS,
    MagicProperty.AUTOTARGETED,
    MagicProperty.CARD_BEING_PLAYED,
  ];

  //
  // Equality and property access
  //

  public override bool Equals(object? obj) =>
    obj is PropertyContainer other
      && _properties.Count == other._properties.Count
      && _properties.All(pair =>
        other._properties.TryGetValue(pair.Key, out var value)
          && object.Equals((object?)pair.Value, (object?)value));

  public override int GetHashCode() => Hash;

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
