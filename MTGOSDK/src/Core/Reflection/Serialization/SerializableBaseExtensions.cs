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
      var serializable = item.ToSerializable();
      yield return (TInterface)BindExpandoToInterface(serializable, proxy.Class);
    }
  }

  private static object BindExpandoToInterface(object obj, Type interfaceType)
  {
    if (obj == null) return null;
    if (interfaceType.IsInstanceOfType(obj)) return obj;

    // If it's an ExpandoObject and the target is an interface, bind it
    if (obj is System.Dynamic.ExpandoObject && interfaceType.IsInterface)
    {
      var expandoDict = (IDictionary<string, object>)obj;
      foreach (var prop in interfaceType.GetProperties())
      {
        if (expandoDict.TryGetValue(prop.Name, out var value) && value != null)
        {
          // Handle collections of interfaces
          if (prop.PropertyType.IsGenericType &&
              typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType) &&
              prop.PropertyType.GetGenericArguments()[0].IsInterface)
          {
            var elemType = prop.PropertyType.GetGenericArguments()[0];
            if (value is System.Collections.IEnumerable enumerable)
            {
              var listType = typeof(List<>).MakeGenericType(elemType);
              var list = (System.Collections.IList)Activator.CreateInstance(listType);
              foreach (var item in enumerable)
              {
                list.Add(BindExpandoToInterface(item, elemType));
              }
              expandoDict[prop.Name] = list;
            }
          }
          // Handle nested interface
          else if (prop.PropertyType.IsInterface && value is System.Dynamic.ExpandoObject)
          {
            expandoDict[prop.Name] = BindExpandoToInterface(value, prop.PropertyType);
          }
        }
      }
      return TypeProxy.As(obj, interfaceType);
    }
    return obj;
  }
#endif
}
