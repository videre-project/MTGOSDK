/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Diagnostics;


namespace MTGOSDK.Core.Reflection.Serialization;

public static class SerializableBaseExtensions
{
  private static readonly ActivitySource s_activitySource = new("MTGOSDK.Core");

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
    using var activity = s_activitySource.StartActivity("SerializeAs-Enumerable");
    activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
    activity?.SetTag("count", enumerable.Count());

    foreach (SerializableBase item in enumerable)
    {
      // Use the instance SerializeAs method directly to preserve enum types
      yield return item.SerializeAs<TInterface>(include, exclude, strict);
    }
  }

  /// <summary>
  /// Serializes a collection to the specified interface type as an async stream.
  /// Each item is yielded after its properties are serialized (in parallel).
  /// </summary>
  /// <typeparam name="TInterface">The interface type to serialize to.</typeparam>
  /// <param name="enumerable">The source collection.</param>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">If true, only properties in the include list are serialized.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An async enumerable that yields serialized items one at a time.</returns>
  /// <remarks>
  /// Use this method for streaming scenarios where you want:
  /// 1. Lazy streaming - items are yielded one at a time
  /// 2. Parallel property fetching - each item's properties are fetched in parallel
  /// 
  /// For batch operations where you want all items at once with maximum parallelism,
  /// use <see cref="SerializationExtensions.SerializeAllAsync{TSource,TInterface}"/> instead.
  /// </remarks>
  public static async IAsyncEnumerable<TInterface> SerializeAsAsync<TInterface>(
    this IEnumerable<SerializableBase> enumerable,
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false,
    [System.Runtime.CompilerServices.EnumeratorCancellation] 
    CancellationToken cancellationToken = default)
  {
    using var activity = s_activitySource.StartActivity("SerializeAsAsync-Enumerable");
    activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
    // Count might not be available for generic IEnumerable without iteration, but usually it is materialized
    if (enumerable is ICollection collection)
    {
       activity?.SetTag("count", collection.Count);
    }
    
    foreach (SerializableBase item in enumerable)
    {
      if (cancellationToken.IsCancellationRequested)
        yield break;
      
      // Each item's properties are fetched in parallel by SerializeAsAsync
      yield return await item.SerializeAsAsync<TInterface>(include, exclude, strict)
        .ConfigureAwait(false);
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
