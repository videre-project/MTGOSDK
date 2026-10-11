/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade.Enums;

public enum TradeError
{
  //#pragma import enum WotC.MtGO.Client.Model.Trade.Enums.ClientTradeErrorEnum begin
  Default = 0,
  EscrowNotAvailible = 1,
  UserNotAuthorized = 2,
  PartnerNotAuthorized = 3,
  UserNotOnline = 4,
  InvalidQuantity = 5,
  DepositListTooLarge = 6,
  AlreadyInTrade = 7,
  NoDepositListSpecified = 8,
  NoPay = 9,
  No2faOnRequestor = 10,
  No2faOnRecipient = 11,
  //#pragma import enum WotC.MtGO.Client.Model.Trade.Enums.ClientTradeErrorEnum end
}
