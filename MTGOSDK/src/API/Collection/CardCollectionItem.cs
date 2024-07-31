/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model;

using CardGrouping = MTGOSDK.API.Collection.CardGrouping<dynamic>;


namespace MTGOSDK.API.Collection;

public class CardCollectionItem(dynamic cardCollectionItem)
    // We override the base instance with the ICardCollectionItem interface.
    : CardQuantityPair(null)
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ICardCollectionItem);

  /// <summary>
  /// Stores an internal reference to the ICardCollectionItem object.
  /// </summary>
  internal override dynamic obj => Bind<ICardCollectionItem>(cardCollectionItem);

  //
  // ICardCollectionItem properties
  //

  public IEnumerable<CardGrouping> ConsumingGroupings =>
    Map<CardGrouping>(@base.ConsumingGroupings);

	public int LockedQuantity => @base.LockedQuantity;
}
