/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// One available choice in a game prompt.
/// </summary>
public interface IPromptOption
{
  ActionType Type { get; }
  string Name { get; }
}
