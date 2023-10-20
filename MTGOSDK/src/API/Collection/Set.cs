/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

public sealed class Set(dynamic set) : DLRWrapper<ICardSet>
{
  /// <summary>
  /// Stores an internal reference to the ICardSet object.
  /// </summary>
  internal override dynamic obj => Proxy<ICardSet>.As(set);

  //
  // ICardSet wrapper properties
  //

  /// <summary>
  /// The unique 2-3 character code for this set.
  /// </summary>
  /// <remarks>
  /// This code may be unique to MTGO and differ from it's regular set code.
  /// </remarks>
  public string Code => @base.Code;

  /// <summary>
  /// The name of the set.
  /// </summary>
  public string Name => @base.Name;

  // public Set CommonSet => new(@base.CommonSet);

  /// <summary>
  /// The release date of the set.
  /// </summary>
  /// <remarks>
  /// For older sets, this will return the paper release date.
  /// </remarks>
  public DateTime ReleaseDate => @base.ReleaseDate;

  /// <summary>
  /// The set product type.
  /// </summary>
  public string Type => Proxy<dynamic>.From(@base).Type.Description;

  // TODO: This is a proxy to enum wrapper class, but we cannot bind to it.
  // public CardSetType Type => CardSetType.GetFromKey(@base.CardSetTypeCd);
  // public CardSetType Type =>
  //   Proxy<CardSetType>.As(@base._EnumMap[@base.CardSetTypeCd]);

  /// <summary>
  /// The set release number ordered by release date.
  /// </summary>
  /// <remarks>
  /// This number begins at 1 and increments by 1 for each set released,
  /// starting with Alpha.
  /// </remarks>
  public int Age => @base.Age;

  /// <summary>
  /// The cards printed in this set.
  /// </summary>
  /// <remarks>
  /// This collection is ordered by the card's catalog id.
  /// </remarks>
  public IEnumerable<Card> Cards =>
    ((IEnumerable<ICardDefinition>)
      @base.Cards)
        .Select(card => new Card(card));

  //
  // ICardSet wrapper methods
  //

  /// <summary>
  /// Whether the set contains the specified card printing.
  /// </summary>
  /// <param name="id">The catalog id of the card to check for.</param>
  /// <returns>True if the set contains the card, otherwise false.</returns>
  public bool ContainsCatalogId(int id) => @base.ContainsCatalogId(id);

  /// <summary>
  /// Whether the set contains the specified card printing.
  /// </summary>
  /// <param name="card">The card to check for.</param>
  /// <returns>True if the set contains the card, otherwise false.</returns>
  public bool ContainsCard(Card card) => ContainsCatalogId(card.Id);

  public static implicit operator string(Set set) => set.Code;
}
