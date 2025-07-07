/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Serialization;

public static class SerializableBaseExtensions
{
#if !MTGOSDKCORE
  /// <summary>
  /// Serializes a collection of objects to the specified interface type.
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
  /// An enumerable of the specified interface type.
  /// </returns>
  /// <remarks>
  /// This method uses reflection to create a dynamic proxy of the specified
  /// interface type and populates it only properties specified by the interface
  /// and the include/exclude lists. This disassociates the object from the
  /// underlying type and prevents reflection on any hidden properties.
  /// </remarks>
  public static IEnumerable<TInterface> SerializeAs<TInterface>(
    this IEnumerable<SerializableBase> enumerable,
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

    foreach (SerializableBase item in enumerable)
    {
      item.SetSerializationProperties(properties, strict: true);
      yield return TypeProxy.As(item.ToSerializable(), proxy.Class);
    }
  }
#endif
}
