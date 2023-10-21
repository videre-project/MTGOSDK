/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;

public sealed class CardQuantityPair(dynamic cardQuantityPair)
    : DLRWrapper<ICardQuantityPair>
{
  /// <summary>
  /// Stores an internal reference to the ICardQuantityPair object.
  /// </summary>
  internal override dynamic obj => cardQuantityPair;

  // TODO: Enable construction of new CardQuantityPair objects by calling the
  // remote constructor.
  //
  // public CardQuantityPair(Card card, int quantity)
  //   : base(RemoteClient.CreateInstance(...))
  // { }

  //
  // ICardQuantityPair derived properties
  //

  public int Id => @base.CatalogId;

  public AttributeAnnotation Annotation => @base.Annotation;

  public Card Card => new(@base.CardDefinition);

  public int Quantity => @base.Quantity;
}
