/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

[Flags]
public enum MagicColors
{
  //#pragma import enum WotC.MtGO.Client.Model.MagicColors begin
  Invalid = 0,
  White = 1,
  Blue = 2,
  Black = 4,
  Red = 8,
  Green = 16,
  Colorless = 32,
  Azorius = 3,
  Orzhov = 5,
  Dimir = 6,
  Izzet = 10,
  Rakdos = 12,
  Golgari = 20,
  Gruul = 24,
  Boros = 9,
  Selesnya = 17,
  Simic = 18,
  Abzan = 21,
  Jeskai = 11,
  Mardu = 13,
  Temur = 26,
  Sultai = 22,
  Naya = 25,
  Jund = 28,
  Grixis = 14,
  Esper = 7,
  Bant = 19,
  WUBR = 15,
  UBRG = 30,
  BRGW = 29,
  RGWU = 27,
  GWUB = 23,
  FiveColor = 31,
  //#pragma import enum WotC.MtGO.Client.Model.MagicColors end
}
