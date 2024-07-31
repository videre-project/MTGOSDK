/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade.Enums;

public enum TradeError
{
	Default,
	EscrowNotAvailible,
	UserNotAuthorized,
	PartnerNotAuthorized,
	UserNotOnline,
	InvalidQuantity,
	DepositListTooLarge,
	AlreadyInTrade,
	NoDepositListSepcified,
	NoPay
}
