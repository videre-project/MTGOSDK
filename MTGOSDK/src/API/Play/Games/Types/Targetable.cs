/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class Targetable(dynamic targetable) : DLRWrapper<ITargetable>
{
  internal override dynamic obj => Bind<ITargetable>(targetable);

  //
  // ITargetable wrapper properties
  //

  public int Id => @base.Id;

  public bool IsTargeted => @base.IsTargeted;

  public string TargetInformation => @base.TargetInformation;

  public GamePlayer Controller => new(@base.Controller);

  //
  // ITargetable wrapper events
  //

  public EventProxy IsTargetedChanged =>
    new(/* ITargetable */ targetable, nameof(IsTargetedChanged));
}
