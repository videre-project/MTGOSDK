/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games.Processors.Snapshots;

/// <summary>
/// Detached player selection flags, using native server player identity.
/// </summary>
public sealed record PlayerSelectable(
  int PlayerIndex,
  bool IsTargetable,
  bool IsTargeted,
  bool IsActive
);
