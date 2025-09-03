/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

[NonSerializable]
public sealed class Set(dynamic set) : DLRWrapper<ICardSet>
{
  /// <summary>
  /// Stores an internal reference to the ICardSet object.
  /// </summary>
  internal override dynamic obj => Bind<ICardSet>(set);

  //
  // ICardSet wrapper properties
  //

  /// <summary>
  /// The unique 2-3 character code for this set.
  /// </summary>
  /// <remarks>
  /// This code may be unique to MTGO and differ from it's regular set code.
  /// </remarks>
  public string Code => field ??= @base.Code;

  /// <summary>
  /// The name of the set.
  /// </summary>
  public string Name => field ??= @base.Name;

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
  public SetType Type => Cast<SetType>(Unbind(this).Type.EnumValue);

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
  public IEnumerable<Card> Cards => Map<Card>(@base.Cards);

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

  public override string ToString() => $"{Name} ({Code})";

  public static implicit operator string(Set set) => set.ToString();
}
