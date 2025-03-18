/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

[NonSerializable]
public sealed class Targetable(dynamic targetable) : DLRWrapper<ITargetable>
{
  internal override dynamic obj => Bind<ITargetable>(targetable);

  internal TargetSet? parentSet = null;

  //
  // ITargetable wrapper properties
  //

  public int Id => @base.Id;

  public bool IsTargeted => @base.IsTargeted || parentSet != null;

  public string TargetInformation => @base.TargetInformation;

  public GamePlayer Controller => new(@base.Controller);

  //
  // ITargetable wrapper methods
  //

  public dynamic ToGameObject() => targetable.GetType().Name switch
  {
    "GameCard" => new GameCard(targetable),
    "GamePlayer" => new GamePlayer(targetable),
    _ => throw new InvalidOperationException(
        $"Unknown targetable type: {targetable.GetType().Name}")
  };

  public override string ToString() => this.ToGameObject().ToString();

  //
  // ITargetable wrapper events
  //

  public EventProxy IsTargetedChanged =>
    new(/* ITargetable */ targetable, nameof(IsTargetedChanged));
}
