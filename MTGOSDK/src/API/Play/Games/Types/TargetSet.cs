/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class TargetSet(dynamic targetSet) : DLRWrapper<ITargetSet>
{
  internal override dynamic obj => Bind<ITargetSet>(targetSet);

  //
  // ITargetSet wrapper properties
  //

  public string ReminderText => @base.AdditionalReminderText;

  public string Description => @base.Description;

  public IList<string> Comments => Map<IList, string>(@base.Comments);

  public int MinimumTargets => @base.MinimumTargets;

  public int MaximumTargets => @base.MaximumTargets;

  public bool IsSet => @base.IsSet;

  public IList<Targetable> LegalTargets =>
    Map<IList, Targetable>(@base.LegalTargets);

  public IList<Targetable> CurrentTargets =>
    Map<IList, Targetable>(@base.CurrentTargets);

  public DictionaryProxy<Targetable, Distribution> Distributions =>
    new(@base.Distributions);

  public bool IsDistributedAmongTargets => @base.IsDistributedAmongTargets;

  public int TotalToAssign => @base.TotalToAssign;

  public int DoubleAmountAfter => @base.DoubleAmountAfter;

  public bool PutOnStackWithoutTargets => @base.PutOnStackWithoutTargets;

  public bool StartDamageAmountsAtOne => @base.StartDamageAmountsAtOne;

  public ActionTargetRequirements TargetRequirements =>
    Cast<ActionTargetRequirements>(Unbind(@base).TargetRequirements);

  public bool VariableLengthAcceptedByPlayer =>
    @base.VariableLengthAcceptedByPlayer;
}
