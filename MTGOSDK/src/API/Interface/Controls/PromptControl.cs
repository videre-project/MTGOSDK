/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Interface.Controls;

/// <summary>
/// A visible prompt-box button without a native prompt action.
/// </summary>
public sealed record PromptControl(
  PromptControlId Id,
  string Name,
  bool Enabled,
  string ButtonType,
  string? Color = null) : IPromptOption
{
  public ActionType Type => ActionType.PromptControl;
}
