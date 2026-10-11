/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Collection;

public enum SetType
{
  //#pragma import enum WotC.MTGO.Common.CardSetTypeEnum begin
  NotSet = 0,
  CoreSet = 1,
  LargeExpansionSet = 2,
  SmallExpansionSet = 3,
  EventTicket = 4,
  TestSet = 5,
  Supplemental = 6,
  PromotionalSet = 7,
  Ancillary = 8,
  FixedAncillary = 9,
  Placeholder = 10,
  //#pragma import enum WotC.MTGO.Common.CardSetTypeEnum end
}
