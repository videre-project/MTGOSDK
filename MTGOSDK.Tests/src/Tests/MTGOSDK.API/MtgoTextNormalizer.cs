/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class MtgoTextNormalizerTests
{
  [TestCase("1#gw-#gw-", "{1}{G/W}{G/W}")]
  [TestCase("2R", "{2}{R}")]
  [TestCase("a", "{10}")]
  [TestCase("#2w-", "{2/W}")]
  [TestCase("#cw-", "{C/W}")]
  [TestCase("#wp-", "{W/P}")]
  [TestCase("#wup", "{W/U/P}")]
  [TestCase("#p--", "{C/P}")]
  public void Test_NormalizeManaCost(string raw, string expected)
  {
    Assert.That(MtgoTextNormalizer.NormalizeManaCost(raw), Is.EqualTo(expected));
  }

  [TestCase(
    "Extort @i(Whenever you cast a spell, you may pay {#wb-}. If you do, each opponent loses 1 life and you gain that much life.)@i",
    "Extort @i(Whenever you cast a spell, you may pay {W/B}. If you do, each opponent loses 1 life and you gain that much life.)@i")]
  [TestCase(
    "Choose up to five {P} worth of modes.\n{PP} @- Exile target nonland permanent.",
    "Choose up to five {P} worth of modes.\n{P}{P} @- Exile target nonland permanent.")]
  [TestCase(
    "@i({p} can be paid with one mana from a snow source.)@i",
    "@i({S} can be paid with one mana from a snow source.)@i")]
  [TestCase(
    "{WUBRG}",
    "{W}{U}{B}{R}{G}")]
  [TestCase(
    "{u/p}",
    "{U/P}")]
  [TestCase(
    "{4pp}",
    "{4}{S}{S}")]
  [TestCase(
    "{H}",
    "{H}")]
  [TestCase(
    "{CHAOS}",
    "{CHAOS}")]
  [TestCase(
    "{L}",
    "{L}")]
  [TestCase(
    "Pay @[Kitchen Finks@:123] {#gw-}.",
    "Pay @[Kitchen Finks@:123] {G/W}.")]
  public void Test_NormalizeText(string raw, string expected)
  {
    Assert.That(MtgoTextNormalizer.NormalizeText(raw), Is.EqualTo(expected));
  }
}
