/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


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
  private readonly ICardDefinition CardDefinition =
    Bind<ICardDefinition>(avatar.CardDefinition);

  //
  // IAvatar wrapper properties
  //

  /// <summary>
  /// The unique identifier of the Avatar resource.
  /// </summary>
  /// <remarks>
  /// This corresponds to the ID of the associated card definition,
  /// which can be fetched with the <see cref="Collection.CardManager"/> class.
  /// </remarks>
  public int Id => CardDefinition.Id;

  /// <summary>
  /// The name of the Avatar.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The associated card definition.
  /// </summary>
  [NonSerializable]
  public Card Card => new(CardDefinition);

  /// <summary>
  /// Whether the Avatar resource has been loaded.
  /// </summary>
  /// <remarks>
  /// This corresponds to the loading state of the card definition's visual
  /// resources, which may be fetched separately after an Avatar is obtained.
  public bool IsLoaded =>
    Try<bool>(() => Unbind(CardDefinition).m_resourceloaded);
}
