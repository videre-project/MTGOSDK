/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

[Flags]
public enum ActionTargetRequirements
{
  None = 0,
  AllTargetsMustShareCreatureType = 1,
  AllTargetsMustShareCardType = 2,
  TargetsMustHaveSameController = 4,
  TargettingUpdateSumValues = 8,
  TargetsMustHaveDifferentControllers = 0x10,
  TargettingUpdateDifferingValues = 0x20,
  TargetingUpdateSameValues = 0x40
}
