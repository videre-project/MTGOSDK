/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum InteractionMode
{
  //#pragma import enum Shiny.Play.Duel.Utility.InteractMode begin
  None = 0,
  SelectTargets = 1,
  SelectAction = 2,
  SelectPlayer = 3,
  SelectFromList = 4,
  OrderTargets = 5,
  ChoosePile = 6,
  WishCard = 7,
  CardSelector = 8,
  Distributing = 9,
  Undefined = 10,
  //#pragma import enum Shiny.Play.Duel.Utility.InteractMode end
}
