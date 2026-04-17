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

  internal CardAction Action = new(targetSet.Action);

  internal CardAction.TargetSetChange? Delta = null;

  internal int Index => Unbind(Action).Targets.IndexOf(Unbind(this));


  //
  // ITargetSet wrapper properties
  //

  public string ReminderText => @base.AdditionalReminderText;

  public string Description => @base.Description;

  public int MinimumTargets => @base.MinimumTargets;

  public int MaximumTargets => @base.MaximumTargets;

  public bool IsSet => CurrentTargets.Count >= MinimumTargets;

  [NonSerializable]
  public IList<Targetable> LegalTargets =>
    Map<IList, Targetable>(@base.LegalTargets);

  public List<Targetable> CurrentTargets { get; internal set; } =
    Map<IList, Targetable>(targetSet.CurrentTargets);

  internal void UpdateTargets(IEnumerable<Targetable> targets) =>
    CurrentTargets = targets.ToList();

  public ActionTargetRequirements TargetRequirements =>
    Cast<ActionTargetRequirements>(Unbind(this).TargetRequirements);
}
