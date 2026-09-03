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

  private readonly int m_quantity = 0;
  private readonly uint? m_annotation;

  internal CardQuantityPair(dynamic cardQuantityPair, int quantity, uint? annotation = null)
    : this(new CardQuantityPair(cardQuantityPair))
  {
    m_quantity = quantity;
    m_annotation = annotation;
  }

  /// <summary>
  /// Stores the values of the ICardQuantityPair object while deferring the
  /// creation of the Card object until it is needed.
  /// </summary>
  private record class CardQuantityPairValues(
    int CatalogId,
    int Quantity,
    string Name,
    uint Annotation)
  {
    public Card CardDefinition =>
      CatalogId > 0
        ? CollectionManager.GetCard(CatalogId)
        : CollectionManager.GetCard(Name);
  }

  public CardQuantityPair(int CatalogId, int Quantity)
    : this(CatalogId, Quantity, 0)
  { }

  public CardQuantityPair(int CatalogId, int Quantity, uint Annotation)
    : this(new CardQuantityPairValues(CatalogId, Quantity, null, Annotation))
  { }

  public CardQuantityPair(string Name, int Quantity, int CatalogId = 0)
    : this(Name, Quantity, CatalogId, 0)
  { }

  public CardQuantityPair(string Name, int Quantity, int CatalogId, uint Annotation)
    : this(new CardQuantityPairValues(CatalogId, Quantity, Name, Annotation))
  { }

  //
  // ICardQuantityPair derived properties
  //

  /// <summary>
  /// The MTGO catalog identifier for the card printing.
  /// </summary>
  public int Id => @base.CatalogId is int id and > 0 ? id : this.Card.Id;

  /// <summary>
  /// The display name of the card.
  /// </summary>
  public string Name => @base.Name ?? this.Card.Name;

  /// <summary>
  /// The card definition associated with this entry.
  /// </summary>
  public Card Card => field ??= new(@base.CardDefinition);

  /// <summary>
  /// The deck-building color annotation bitmask associated with this entry.
  /// </summary>
  /// <remarks>
  /// MTGO uses this value to record the deck-building color assigned to a card.
  /// Because differently annotated copies of the same catalog item are stored as
  /// separate entries, the annotation should be preserved when copying or
  /// modifying deck contents. A value of zero means no color is assigned.
  /// </remarks>
  public uint Annotation =>
    m_annotation ?? CastRemoteValue<uint>(Unbind(this).Annotation);

  /// <summary>
  /// The number of copies represented by this entry.
  /// </summary>
  public int Quantity => m_quantity != 0 ? m_quantity : @base.Quantity;
}
