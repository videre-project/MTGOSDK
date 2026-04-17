/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.EventArgs;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Emits <see cref="DamageAssignmentEventArgs"/> when the server sends
/// DamageAssignment state elements during combat damage resolution.
/// </summary>
public sealed class DamageAssignmentProcessor : IGameStateProcessor
{
  public void Initialize(Game game) { }

  public void Process(GameContext context)
  {
    if (context.DamageAssignments.Count == 0) return;

    context.Emit(new DamageAssignmentEventArgs(context.DamageAssignments));
  }
}
