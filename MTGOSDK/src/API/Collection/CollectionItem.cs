/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

public class CollectionItem<T>(dynamic collectionItem)
    : DLRWrapper<IMagicEntityDefinition>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IMagicEntityDefinition);

  /// <summary>
  /// Stores an internal reference to the CollectionItem object.
  /// </summary>
  [RuntimeInternal]
  internal override dynamic obj => collectionItem;

  //
  // IMagicEntityDefinition properties
  //

  public int Id => @base.Id;

  public string Name => field ??= @base.Name;

  public string Description => @base.Description;

  public bool IsOpenable => @base.IsOpenable;

  public bool IsSealedProduct => @base.IsSealedProduct;

  public bool IsDigitalObject => @base.IsDigitalObject;

  public bool IsBooster => @base.IsBooster;

  public bool IsCard => @base.IsCard;

  public bool IsTicket => @base.IsTicket;

  public bool IsTradeable => @base.IsTradable; // Note: This is a typo in MTGO.

  public bool HasPremiumOdds => @base.HasPremiumOdds;
}
