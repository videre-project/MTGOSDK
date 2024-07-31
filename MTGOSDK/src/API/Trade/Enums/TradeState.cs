/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade.Enums;

public enum TradeState
{
  Uninitialized,
  InviteSelectBinder,
  InviteSent,
  InviteReceived,
  InviteAccepted,
  InviteCollectionSent,
  NegotiateNoDeposit,
  NegotiateDepositSubmittedLocal,
  NegotiateDepositSubmittedOther,
  NegotiateDepositReceivedLocal,
  NegotiateDepositReceivedOther,
  NegotiateDepositReceivedBoth,
  NegotiateClearSent,
  ApprovalNone,
  ApprovalSubmittedLocal,
  ApprovalSubmittedOther,
  ApprovalReceivedLocal,
  ApprovalReceivedOther,
  ApprovalReceivedBoth,
  ApprovalClearSent,
  PackOpenSetupRequested,
  PackOpenUpdatelistSent,
  PackOpenProcessing,
  CancelRequested,
  Closed
}
