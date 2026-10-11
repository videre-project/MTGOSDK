/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Interface.Controls;

/// <summary>
/// A visible button parented to a duelscene's prompt box.
/// </summary>
public sealed record PromptButton(
  string? Name,
  string? Color,
  bool Enabled,
  PromptControlId Id,
  string ButtonType,
  bool HasGameAction
);
