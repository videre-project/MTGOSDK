/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

public class CardQuantityPair(dynamic cardQuantityPair)
    : DLRWrapper<ICardQuantityPair>
{
  /// <summary>
  /// Stores an internal reference to the ICardQuantityPair object.
  /// </summary>
  internal override dynamic obj => cardQuantityPair;

  /// <summary>
  /// Stores the values of the ICardQuantityPair object while deferring the
  /// creation of the Card object until it is needed.
  /// </summary>
  private record class CardQuantityPairValues(
    int CatalogId,
    int Quantity,
    string Name)
  {
    public Card CardDefinition =>
      field ??= CollectionManager.GetCard(CatalogId);
  }

  public CardQuantityPair(int CatalogId, int Quantity, string Name)
    : this(new CardQuantityPairValues(CatalogId, Quantity, Name))
  { }

  //
  // ICardQuantityPair derived properties
  //

  public int Id => @base.CatalogId;

  public string Name => field ??= Try(() => @base.Name, () => this.Card.Name);

  public Card Card => field ??= new(@base.CardDefinition);

  public int Quantity => @base.Quantity;
}
