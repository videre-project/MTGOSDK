/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

[Flags]
public enum MagicColors
{
  Invalid = 0,
  White = 1,
  Blue = 2,
  Black = 4,
  Red = 8,
  Green = 0x10,
  Colorless = 0x20,
  Azorius = 3,
  Orzhov = 5,
  Dimir = 6,
  Izzet = 0xA,
  Rakdos = 0xC,
  Golgari = 0x14,
  Gruul = 0x18,
  Boros = 9,
  Selesnya = 0x11,
  Simic = 0x12,
  Abzan = 0x15,
  Jeskai = 0xB,
  Mardu = 0xD,
  Temur = 0x1A,
  Sultai = 0x16,
  Naya = 0x19,
  Jund = 0x1C,
  Grixis = 0xE,
  Esper = 7,
  Bant = 0x13,
  WUBR = 0xF,
  UBRG = 0x1E,
  BRGW = 0x1D,
  RGWU = 0x1B,
  GWUB = 0x17,
  FiveColor = 0x1F
}
