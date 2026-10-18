/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Interface.Controls;

/// <summary>
/// Position of a button in its live prompt-box collection.
/// </summary>
public readonly record struct PromptControlId(
  PromptButtonGroup Group,
  int Index
);
