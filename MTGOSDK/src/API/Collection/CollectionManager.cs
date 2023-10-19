/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Reflection;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API.Collection;

public static class CollectionManager
{
  //
  // CardDataManager wrapper methods
  //

  /// <summary>
  /// Manages the client's set information and card definitions.
  /// </summary>
  private static readonly ICardDataManager s_cardDataManager =
    ObjectProvider.Get<ICardDataManager>();

  /// <summary>
  /// Returns a card object by the given catalog id.
  /// </summary>
  /// <param name="id">The catalog id of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given catalog id.
  /// </exception>
  public static Card GetCard(int id) =>
    new(
      s_cardDataManager.GetCardDefinitionForCatId(id)
        ?? throw new KeyNotFoundException(
            $"No card found with catalog id #{id}.")
    );

  /// <summary>
  /// Returns a card object by the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given name.
  /// </exception>
  public static Card GetCard(string cardName) =>
    GetCard(
      GetCardIds(cardName).First()
        // ?? throw new KeyNotFoundException(
        //     $"No card found with name \"{cardName}\".")
    );

  /// <summary>
  /// Returns a list of catalog ids for the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of catalog ids.</returns>
  public static IList<int> GetCardIds(string cardName) =>
    s_cardDataManager.GetCatalogIdsForNameInPreferentialOrder(cardName, true);

  // TODO: Fix type casting of nested types, i.e. Dictionary<string, CardSet>.
  // public static Set GetSet(string setCode)
  // {
  //   var setCodeDict = Proxy<dynamic>.From(s_cardDataManager).AllCardSetsByCode;
  //   if (!setCodeDict.TryGetValue(setCode, out dynamic set) || set is null)
  //     throw new KeyNotFoundException($"No set found with code \"{setCode}\".");
  //
  //   return new(set);
  // }
}
