/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.ResourceManagement;


namespace MTGOSDK.API.Users;

/// <summary>
/// Represents a user's avatar resource.
/// </summary>
public sealed class Avatar(dynamic avatar) : DLRWrapper<IAvatar>
{
  /// <summary>
  /// Stores an internal reference to the IAvatar object.
  /// </summary>
  internal override dynamic obj => avatar; // Input obj is not type-casted.

  /// <summary>
  /// The associated visual resource for the Avatar.
  /// </summary>
  private readonly IVisualResource Image = Bind<IVisualResource>(avatar.Image);

  //
  // IAvatar wrapper properties
  //

  /// <summary>
  /// The name of the Avatar.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The associated card definition.
  /// </summary>
  public Card Card => new(@base.CardDefinition);

  //
  // IVisualResource wrapper properties
  //

  /// <summary>
  /// The unique identifier of the Avatar resource.
  /// </summary>
  [Default(-1)]
  public int Id => Image.Id;

  /// <summary>
  /// The Uri of the Avatar resource.
  /// </summary>
  public Uri View => Cast<Uri>(Image.View);

  //
  // IVisualResource wrapper events
  //

  public EventProxy ViewChanged =
    new(/* IVisualResource */ avatar.Image, nameof(ViewChanged));
}
