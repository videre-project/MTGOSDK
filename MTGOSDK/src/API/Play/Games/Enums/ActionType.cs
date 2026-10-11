/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum ActionType
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.ActionType begin
  Invalid = 0,
  ChooseOption = 1,
  CardAction = 2,
  OrderTargets = 3,
  DistributeAmongTargets = 4,
  ChooseNumber = 5,
  ChoosePlayer = 6,
  ChoosePile = 7,
  PayMana = 8,
  SelectFromList = 9,
  WishCard = 10,
  OKWithManaBurnPrompt = 11,
  CardSelector = 12,
  //#pragma import enum WotC.MtGO.Client.Model.Play.ActionType end

}
