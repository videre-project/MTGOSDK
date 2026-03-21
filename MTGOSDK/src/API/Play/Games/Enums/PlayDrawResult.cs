/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents the choice to play first or draw first (go second).
/// </summary>
public enum PlayDrawResult
{
  /// <summary>
  /// The player chose to play first.
  /// </summary>
  Play,
  /// <summary>
  /// The player chose to draw first (go second).
  /// </summary>
  Draw,
}
