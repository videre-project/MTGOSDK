/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum CardAssociation
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.CardAssociation begin
  Invalid = 0,
  ActionTriggering = 1,
  ChosenSource = 2,
  ChosenPlayer = 3,
  HauntedTarget = 4,
  ImprintedCard = 5,
  MustBlockIfAble = 6,
  MustAttackIfAble = 7,
  RemovedFromGameBy = 8,
  TriggeringSource = 9,
  ActionTarget = 10,
  ActionTargetedBy = 11,
  ActionTargeting = 12,
  ActionTargetingSource = 13,
  AttackVictim = 14,
  BlockingTarget = 15,
  Soulbond = 16,
  BandedWith = 17,
  BandingWith = 18,
  EquippedTo = 19,
  EquippedWith = 20,
  GraftTo = 21,
  GraftFrom = 22,
  DrawArrowFrom = 23,
  DrawArrowTo = 24,
  PotentiallyAffected = 25,
  //#pragma import enum WotC.MtGO.Client.Model.Play.CardAssociation end
}
