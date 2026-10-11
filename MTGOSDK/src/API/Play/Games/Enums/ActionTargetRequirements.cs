/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

[Flags]
public enum ActionTargetRequirements
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.Enums.ActionTargetRequirements begin
  None = 0,
  AllTargetsMustShareCreatureType = 1,
  AllTargetsMustShareCardType = 2,
  TargetsMustHaveSameController = 4,
  TargettingUpdateSumValues = 8,
  TargetsMustHaveDifferentControllers = 16,
  TargettingUpdateDifferingValues = 32,
  TargetingUpdateSameValues = 64,
  TargetsMustHaveSameToughness = 128,
  TargetsMustHaveDifferentNames = 256,
  TargetsMustSharePermanentType = 512,
  //#pragma import enum WotC.MtGO.Client.Model.Play.Enums.ActionTargetRequirements end
}
