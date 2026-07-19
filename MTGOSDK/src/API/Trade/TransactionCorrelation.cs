/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Trade;

/// <summary>
/// Correlates an MTGO transaction callback with its operation and escrow.
/// </summary>
/// <param name="Timestamp">
/// The client-observed timestamp of the underlying MTGO callback.
/// </param>
/// <param name="OperationId">
/// The operation identifier supplied by MTGO, when available.
/// </param>
/// <param name="EscrowToken">
/// The stable escrow token supplied by MTGO, when available.
/// </param>
public readonly record struct TransactionCorrelation(
  DateTime Timestamp,
  ulong? OperationId,
  Guid? EscrowToken
);
