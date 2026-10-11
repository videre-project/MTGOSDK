/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade.Enums;

public enum TradeState
{
  //#pragma import enum WotC.MtGO.Client.Model.Trade.Enums.TradeState begin
  Uninitialized = 0,
  InviteSelectBinder = 1,
  InviteSent = 2,
  InviteReceived = 3,
  InviteAccepted = 4,
  InviteCollectionSent = 5,
  NegotiateNoDeposit = 6,
  NegotiateDepositSubmittedLocal = 7,
  NegotiateDepositSubmittedOther = 8,
  NegotiateDepositReceivedLocal = 9,
  NegotiateDepositReceivedOther = 10,
  NegotiateDepositReceivedBoth = 11,
  NegotiateClearSent = 12,
  ApprovalNone = 13,
  ApprovalSubmittedLocal = 14,
  ApprovalSubmittedOther = 15,
  ApprovalReceivedLocal = 16,
  ApprovalReceivedOther = 17,
  ApprovalReceivedBoth = 18,
  ApprovalClearSent = 19,
  PackOpenSetupRequested = 20,
  PackOpenUpdatelistSent = 21,
  PackOpenProcessing = 22,
  CancelRequested = 23,
  Closed = 24,
  //#pragma import enum WotC.MtGO.Client.Model.Trade.Enums.TradeState end
}
