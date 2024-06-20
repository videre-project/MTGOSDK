/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.API.Collection;

[Flags]
public enum DeckRegion : long
{
  NotSet      = 0L,
  MainDeck    = 1L,
  Sideboard   = 2L,
  CommandZone = 4L,
  Planechase  = 8L,
  Vanguard    = 0x10L, // 16
  Hidden      = 0x20L, // 32
}
