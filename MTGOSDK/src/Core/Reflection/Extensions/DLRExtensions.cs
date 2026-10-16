/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Linq.Expressions;

using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Reflection.Extensions;

public static class DLRExtensions
{
  /// <summary>
  /// Filters a remote collection to items assignable to the specified type.
  /// </summary>
  /// <typeparam name="T">The required runtime type or interface.</typeparam>
  /// <param name="collection">The remote collection to filter.</param>
  /// <returns>An enumerable of assignable items.</returns>
  public static DynamicRemoteObject WhereAssignableTo<T>(
    this DynamicRemoteObject collection)
  {
    var batch = new RemoteBatch();
    var source = batch.Input(collection);

    // Packaging can merge SDK contract metadata into a different assembly.
    // Resolve the corresponding native contract by its full name.
    var result = batch.WhereAssignableTo(source, new RemoteBatchType(typeof(T).FullName!));
    return batch.ExecuteReference(result, RemoteClient.@client)!.Dynamify();
  }

  /// <summary>
  /// Filters a remote collection using an expression-based predicate.
  /// The predicate is parsed and executed on the remote side for performance.
  /// </summary>
  /// <typeparam name="T">The element type (used for expression parsing).</typeparam>
  /// <param name="collection">The remote collection to filter.</param>
  /// <param name="predicate">A simple binary comparison expression (e.g., e => e.Foo > 5).</param>
  /// <returns>An enumerable of matching items.</returns>
  /// <remarks>
  /// This method avoids per-item IPC calls by executing the filter on the remote side.
  /// Supported operators: ==, !=, &gt;, &gt;=, &lt;, &lt;=
  /// </remarks>
  public static DynamicRemoteObject Filter<T>(
    this DynamicRemoteObject collection,
    Expression<Func<T, bool>> predicate)
  {
    var batch = new RemoteBatch();
    var source = batch.Input(collection);
    RemoteBatchSlot result;
    if (ExpressionParser.TryParsePropertyComparison(predicate,
      out string leftPropertyName, out ComparisonOperator comparison, out string rightPropertyName))
    {
      result = batch.WhereProperty(source, leftPropertyName, comparison, rightPropertyName);
    }
    else
    {
      var (propertyName, op, value) = ExpressionParser.ParsePredicate(predicate);
      result = batch.Where(source, propertyName, op, batch.Input(value));
    }
    return batch.ExecuteReference(result, RemoteClient.@client)!.Dynamify();
  }

  /// <summary>
  /// Sorts a remote collection by a property expression.
  /// The sort is executed on the remote side for performance.
  /// </summary>
  /// <typeparam name="T">The element type (used for expression parsing).</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="collection">The remote collection to sort.</param>
  /// <param name="keySelector">A property access expression (e.g., e => e.StartTime).</param>
  /// <param name="descending">Whether to sort in descending order.</param>
  /// <returns>A sorted collection (dynamic).</returns>
  /// <remarks>
  /// This method avoids per-item IPC calls by executing the sort on the remote side.
  /// </remarks>
  public static DynamicRemoteObject Sort<T, TKey>(
    this DynamicRemoteObject collection,
    Expression<Func<T, TKey>> keySelector,
    bool descending = false)
  {
    var batch = new RemoteBatch();
    var source = batch.Input(collection);
    var result = batch.OrderBy(source, ExpressionParser.ParseKeySelector(keySelector), descending);
    return batch.ExecuteReference(result, RemoteClient.@client)!.Dynamify();
  }
}
