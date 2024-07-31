/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade.Enums;

public enum TradeFinalState
{
	EscrowNotAvailable,
	TradeNotFinal,
	UserCanceledInvitation,
	OtherCanceledInvitation,
	InvitationExpired,
	UserDeclinedInvitation,
	OtherDeclinedInvitation,
	OtherBusyTrading,
	TradeExpired,
	UserCanceledTrade,
	OtherCanceledTrade,
	OtherLoggedOut,
	InvalidParticipant,
	TradeComplete,
	TradeIncomplete,
	OpenpackNotPermitted,
	OpenpackInsufficientQuantity,
	OpenpackComplete,
	ReceivedUnexpectedMessage,
	ReceivedErroneousMessage,
	TradeCompleteWithErrors,
	Invalid,
	InvalidState,
	InsufficientQuantity,
	InsufficientPermission,
	GrantUnsuccessful,
	ErrorReceived,
	NoPayMode,
	AlreadyTrading,
	FinalListMismatch
}
