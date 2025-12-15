/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Collection;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;


namespace MTGOSDK.API.Interface.ViewModels;

public sealed class DetailsViewModel(dynamic detailsViewModel)
    : DLRWrapper<ICardDefinition>
{
  /// <summary>
  /// Stores an internal reference to the DetailsViewModel object.
  /// </summary>
  internal override dynamic obj => detailsViewModel;

  /// <summary>
  /// Creates a new remote instance of the BasicToastViewModel class.
  /// </summary>
  private static BasicToastViewModel NewInstance(Card card, bool owned)
  {
    var annotation = RemoteClient.CreateEnum<AttributeAnnotation>("NotSet");
    var instance = RemoteClient.CreateInstance(
      new TypeProxy<Shiny.CardManager.ViewModels.DetailsViewModel>(),
      Array.Empty<object>()
    );
    instance.Initialize(card, owned, true, annotation);
    return new(instance);
  }

  /// <summary>
  /// Creates a new remote instance of the BasicToastViewModel class.
  /// </summary>
  public DetailsViewModel(Card card, bool owned = true)
      : this(NewInstance(card, owned))
  {}
}
