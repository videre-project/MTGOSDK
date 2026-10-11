/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade.Enums;

public enum TradeFinalState
{
  //#pragma import enum WotC.MtGO.Client.Model.Trade.Enums.TradeFinalState begin
  EscrowNotAvailable = 0,
  TradeNotFinal = 1,
  UserCanceledInvitation = 2,
  OtherCanceledInvitation = 3,
  InvitationExpired = 4,
  UserDeclinedInvitation = 5,
  OtherDeclinedInvitation = 6,
  OtherBusyTrading = 7,
  TradeExpired = 8,
  UserCanceledTrade = 9,
  OtherCanceledTrade = 10,
  OtherLoggedOut = 11,
  InvalidParticipant = 12,
  TradeComplete = 13,
  TradeIncomplete = 14,
  OpenpackNotPermitted = 15,
  OpenpackInsufficientQuantity = 16,
  OpenpackComplete = 17,
  ReceivedUnexpectedMessage = 18,
  ReceivedErroneousMessage = 19,
  TradeCompleteWithErrors = 20,
  Invalid = 21,
  InvalidState = 22,
  InsufficientQuantity = 23,
  InsufficientPermission = 24,
  GrantUnsuccessful = 25,
  ErrorReceived = 26,
  NoPayMode = 27,
  AlreadyTrading = 28,
  FinalListMismatch = 29,
  //#pragma import enum WotC.MtGO.Client.Model.Trade.Enums.TradeFinalState end
}
