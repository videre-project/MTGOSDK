/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;

public sealed class Collection(ICollectionGrouping collection)
    : CollectionItem<ICollectionGrouping>
{
  /// <summary>
  /// Stores an internal reference to the ICollectionGrouping object.
  /// </summary>
  internal override dynamic obj => collection;

  public Collection() : this(CollectionManager.GetCollection()) { }

  //
  // ICollectionGrouping wrapper properties
  //
}
