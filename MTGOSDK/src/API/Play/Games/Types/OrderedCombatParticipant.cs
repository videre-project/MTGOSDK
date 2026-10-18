/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public readonly struct OrderedCombatParticipant(int order, GameCard target)
{
  public readonly int Order = order;
  public readonly GameCard Target = target;
}
