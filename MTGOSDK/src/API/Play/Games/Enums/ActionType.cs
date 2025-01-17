/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum ActionType
{
  Invalid,
  ChooseOption,
  CardAction,
  OrderTargets,
  DistributeAmongTargets,
  ChooseNumber,
  ChoosePlayer,
  ChoosePile,
  PayMana,
  SelectFromList,
  WishCard,
  OKWithManaBurnPrompt,
  CardSelector
}
