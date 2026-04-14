/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class Distribution(dynamic distribution)
    : DLRWrapper<IDistribution>
{
  internal override dynamic obj =>
    distribution is DistributionPartial partial
      ? partial
      : Bind<IDistribution>(distribution);

  //
  // IDistribution wrapper properties
  //

  public Targetable Target => new(@base.Target);

  public int Value => @base.Value;

  public int Minimum => @base.Minimum;

  public int Maximum => @base.Maximum;
}
