/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a card association (e.g. Target, Attacker, Effect source, etc.)
/// </summary>
public class GameCardAssociation(dynamic gameCardAssociation)
    : DLRWrapper<IGameCardAssociation>
{
  internal override dynamic obj =>
    Bind<IGameCardAssociation>(gameCardAssociation);

  //
  // IGameCardAssociation wrapper properties
  //

  /// <summary>
  /// Represents a card association (e.g. ChosenPlayer, TriggeringSource, etc.).
  /// </summary>
  public CardAssociation Association =>
    Cast<CardAssociation>(Unbind(@base).Value);

  /// <summary>
  /// The ID of the associated target.
  /// </summary>
  public int TargetId => @base.AssociatedTarget.Id;
}
