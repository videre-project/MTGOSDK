/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Batching;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a card association (e.g. Target, Attacker, Effect source, etc.)
/// </summary>
public class GameCardAssociation(dynamic gameCardAssociation)
    : DLRWrapper<IGameCardAssociation>
{
  private readonly CardAssociation? _association;
  private readonly int _targetId;

  internal GameCardAssociation(CardAssociation association, int targetId) : this((object?)null)
  {
    _association = association;
    _targetId = targetId;
  }

  internal override dynamic obj =>
    gameCardAssociation is RemoteBatchObject captured ? captured
      : Bind<IGameCardAssociation>(gameCardAssociation);

  //
  // IGameCardAssociation wrapper properties
  //

  /// <summary>
  /// Represents a card association (e.g. ChosenPlayer, TriggeringSource, etc.).
  /// </summary>
  public CardAssociation Association =>
    _association ?? (gameCardAssociation is RemoteBatchObject captured
      ? captured.Get<CardAssociation>("Value")
      : Cast<CardAssociation>(Unbind(this).Value));

  /// <summary>
  /// The ID of the associated target.
  /// </summary>
  public int TargetId => _association != null ? _targetId
    : gameCardAssociation is RemoteBatchObject captured
      ? captured.Get<RemoteBatchObject>("AssociatedTarget").Get<int>("Id")
      : @base.AssociatedTarget.Id;
}
