/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Reflection;


namespace MTGOSDK.Core.Reflection.Serialization;

public abstract class SerializableBase : IJsonSerializable
{
  private static readonly ConcurrentDictionary<Type, PropertyFilter> k__PropertyFilters = new();

  private Type k__DerivedType = null!;
  private IList<PropertyInfo>? k__SerializableProperties;

  internal IList<PropertyInfo> SerializableProperties =>
    k__SerializableProperties ??=
      k__PropertyFilters
        .GetOrAdd(k__DerivedType ??= this.GetType(), _ => new(k__DerivedType))
        .Properties;

  /// <summary>
  /// Configures which properties to include or exclude from serialization.
  /// </summary>
  /// <param name="derivedType">The type of the object to serialize.</param>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">
  /// If true, only the properties in the include list will be serialized.
  /// </param>
  public static void SetSerializationProperties(
    Type derivedType,
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    // Create a new PropertyFilter with the specified properties.
    var filter = new PropertyFilter(include, exclude, strict, derivedType);
    k__PropertyFilters.AddOrUpdate(derivedType, filter, (_, _) => filter);
  }

  /// <summary>
  /// Configures which properties to include or exclude from serialization.
  /// </summary>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">
  /// If true, only the properties in the include list will be serialized.
  /// </param>
  public void SetSerializationProperties(
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    // Set the serialization properties for the current instance.
    k__DerivedType = this.GetType();
    var filter = new PropertyFilter(include, exclude, strict, k__DerivedType);
    k__SerializableProperties = filter.Properties;
  }

#if !MTGOSDKCORE
  /// <summary>
  /// Serializes the object to a specified interface type.
  /// </summary>
  /// <typeparam name="TInterface">
  /// The interface type to serialize to.
  /// </typeparam>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">
  /// If true, only the properties in the include list will be serialized.
  /// </param>
  /// <returns>
  /// An object of the specified interface type with the serialized properties.
  /// </returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the specified type is not an interface.
  /// </exception>
  /// <remarks>
  /// This method uses reflection to create a dynamic proxy of the specified
  /// interface type and populates it only properties specified by the interface
  /// and the include/exclude lists. This disassociates the object from the
  /// underlying type and prevents reflection on any hidden properties.
  /// </remarks>
  public TInterface SerializeAs<TInterface>(
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    TypeProxy<dynamic> proxy = new(typeof(TInterface));
    if (!proxy.IsInterface)
    {
      throw new ArgumentException(
        $"The specified type {typeof(TInterface)} must be an interface.");
    }

    // Get all properties of the specified interface.
    var filter = new PropertyFilter(include, exclude, strict, proxy.Class);
    IList<string> properties = filter.Properties.Select(p => p.Name).ToList();

    this.SetSerializationProperties(properties, strict: true);
    return TypeProxy.As(this.ToSerializable(), proxy.Class);
  }
#endif
}
