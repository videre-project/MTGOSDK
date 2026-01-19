/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Text.RegularExpressions;
using RegexMatch = System.Text.RegularExpressions.Match;

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;

public abstract partial class CardGrouping<T> : DLRWrapper<ICardGrouping>
{
  //
  // ICardGrouping derived properties
  //

  /// <summary>
  /// The unique identifier for this grouping.
  /// </summary>
  public int Id => @base.NetDeckId;

  /// <summary>
  /// The user-defined name for this grouping.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The format this grouping is associated with. (e.g. Standard, Historic, etc.)
  /// </summary>
  public PlayFormat? Format => Optional<PlayFormat>(@base.Format);

  /// <summary>
  /// The timestamp of the last modification to this grouping.
  /// </summary>
  public DateTime Timestamp => @base.Timestamp;

  /// <summary>
  /// The total number of cards contained in this grouping.
  /// </summary>
  public int ItemCount => @base.ItemCount;

  /// <summary>
  /// The maximum number of cards that can be contained in this grouping.
  /// </summary>
  public int MaxItems => @base.MaxItems;

  /// <summary>
  /// The hash of the contents of this grouping.
  /// </summary>
  public string Hash => @base.CurrentHash;

  /// <summary>
  /// The items contained in this grouping.
  /// </summary>
  /// <remarks>
  /// Except for the <see cref="Binder"/> and <see cref="Collection"/> classes,
  /// this property will only return items with a quantity greater than zero.
  /// </remarks>
  public IEnumerable<CardQuantityPair> Items =>
    @base.ShouldRemoveZeroQuantityItems
      ? Map<CardQuantityPair>(
        // If specified, filter out items with zero quantity.
        ((DynamicRemoteObject)Unbind(@base).Items)
          .Filter<ICardQuantityPair>(item => item.Quantity > 0))
      : Map<CardQuantityPair>(@base.Items);

  /// <summary>
  /// The unique identifiers of the items contained in this grouping.
  /// </summary>
  public IEnumerable<int> ItemIds => Map<int>(@base.ItemIds);

  //
  // ICardGrouping derived methods
  //

  [GeneratedRegex(
    @"^\s*-\s*Qty:\s*([1-9]\d*)\s*Id:\s*(\d+)\s*Name:\s*(.*)$",
    RegexOptions.Multiline)]
  private static partial Regex ParseQuantitiesAndIdsRegex();

  private static IEnumerable<(int, int, string)> ParseItems(string debugData)
  {
    var regex = ParseQuantitiesAndIdsRegex();
    var matches = regex.Matches(debugData);
    foreach (RegexMatch match in matches)
    {
      if (match.Groups.Count == 1 + 3) // Ensure all 3 groups are captured
      {
        if (int.TryParse(match.Groups[1].Value, out int qty) &&
            int.TryParse(match.Groups[2].Value, out int id) &&
            match.Groups[3].Success &&
            match.Groups[3].Value?.Trim() is string name)
        {
          yield return (id, qty, name);
        }
      }
      else
      {
        throw new InvalidOperationException(
          $"Invalid match group count: {match.Groups.Count} in {nameof(ParseItems)}.");
      }
    }
  }

  /// <summary>
  /// Parses the grouping's items from the object's debug data, returning a
  /// frozen collection of partial <see cref="CardQuantityPair"/> objects.
  /// </summary>
  /// <remarks>
  /// This is useful for accessing large collections or binders without the
  /// overhead of the <see cref="CollectionManager"/> retrieving all the cards.
  /// </remarks>
  public IList<CardQuantityPair> GetFrozenCollection =>
    Map<IList, CardQuantityPair>(
      ParseItems(@base.DebugData()),
      Lambda(item => new CardQuantityPair(item.Item1, item.Item2, item.Item3)));

  //
  // Batch serialization methods
  //

  /// <summary>
  /// Serializes all items in this grouping to the specified interface type using
  /// cross-card batch fetching for optimal performance.
  /// </summary>
  /// <typeparam name="TInterface">The interface type to serialize items as.</typeparam>
  /// <param name="maxItems">Maximum number of items to serialize (0 = no limit).</param>
  /// <returns>Enumerable of serialized items implementing TInterface.</returns>
  /// <remarks>
  /// This method uses a single IPC call to fetch all items' properties, avoiding
  /// per-item overhead. For large collections, this can be 5-10x faster than
  /// iterating and calling SerializeAs on each item individually.
  /// </remarks>
  public IEnumerable<TInterface> SerializeItemsAs<TInterface>(int maxItems = 0)
    where TInterface : class
    => SerializeCollectionAs<TInterface, Card>(
        nameof(ICardGrouping.Items),
        nameof(ICardQuantityPair.CardDefinition),
        maxItems);
}
