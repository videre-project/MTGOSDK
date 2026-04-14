/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Marker for processors that are safe to run on same-nonce (intermediate)
/// snapshots — i.e., those that track entity state changes rather than
/// interaction/prompt events.
/// </summary>
public interface IIntermediateTickProcessor : IGameStateProcessor { }
