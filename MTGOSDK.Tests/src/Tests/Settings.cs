/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOSDK.API;
using MTGOSDK.API.Settings;


namespace MTGOSDK.Tests;

public class Settings : SettingsValidationFixture
{
  [Test]
  public void Test_SettingsService()
  {
    // Assuming that the last user is the same as the current user,
    // verify that user settings are being read correctly.
    var lastUserName = SettingsService.GetSetting(Setting.LastLoginName);
    Assert.That(lastUserName, Is.Not.EqualTo(string.Empty));
    Assert.That(lastUserName, Is.EqualTo(Client.CurrentUser.Name));

    // Verify that the last EULA version agreed to is the current
    // version after the client has started.
    var lastEulaVersion = SettingsService.GetSetting<Version>(Setting.LastEULAVersionNumberAgreedTo);
    Assert.That(lastEulaVersion, Is.Not.Null);
    Assert.That(lastEulaVersion, Is.EqualTo(Client.Version));

    // Check that Setting.Invalid throws an exception to guard
    // against invalid settings.
    Assert.That(() => SettingsService.GetSetting(Setting.Invalid),
                Throws.TypeOf<KeyNotFoundException>());

    var phaseLadder = SettingsService.GetDefaultSetting<bool>(Setting.ShowPhaseLadder);
    Assert.That(phaseLadder, Is.True);
  }

  [TestCase(Setting.ShowBigCardWindow, true)]
  [TestCase(Setting.AlwaysShowRedZone, false)]
  [TestCase(Setting.ShowPhaseLadder, true)]
  [TestCase(Setting.EnableAnimation, true)]
  [TestCase(Setting.AutoSizeCards, true)]
  [TestCase(Setting.ManuallyChosenBattlefieldCardSize, 50)]
  [TestCase(Setting.RedZoneDefaultSize, 40.0)]
  [TestCase(Setting.ShowChatInDuel, false)]
  [TestCase(Setting.ShowLogInDuel, true)]
  [TestCase(Setting.IgnoreChatNotifications, false)]
  [TestCase(Setting.AlwaysDisableBluffing, false)]
  [TestCase(Setting.AlwaysFastStacking, false)]
  public void Test_UserSettings(Setting key, object defaultValue)
  {
    ValidateSetting(key, defaultValue);
    object setting = SettingsService.GetSetting(key);

    PrimitiveSetting<object> entry = SettingsService.UserSettings[key];
    object currentValue = entry.Value;
    Assert.That(currentValue, Is.Not.Null);
    Assert.That(currentValue.GetType(), Is.EqualTo(defaultValue.GetType()));
    Assert.That(currentValue, Is.EqualTo(setting));
  }

  [Test]
  [TestCase(Setting.LastLoginName, "")]
  [TestCase(Setting.LastEULAVersionNumberAgreedTo, "")]
  [TestCase(Setting.ShowAccountActivationDialog, default(bool))]
  // [TestCase(Setting.AgeGateBirthDate, "0001-01-01")]
  [TestCase(Setting.JoinBegoneWarning, false)]
  public void Test_ApplicationSettings(Setting key, object defaultValue)
  {
    ValidateSetting(key, defaultValue);
    object setting = SettingsService.GetSetting(key);

    PrimitiveSetting<object> entry = SettingsService.ApplicationSettings[key];
    object currentValue = entry.Value;
    Assert.That(currentValue, Is.Not.Null);
    Assert.That(currentValue.GetType(),
        Is.EqualTo(defaultValue?.GetType() ?? setting.GetType()));
    Assert.That(currentValue, Is.EqualTo(setting));
  }
}

public class SettingsValidationFixture : BaseFixture
{
  public void ValidateSetting(Setting key, object defaultValue)
  {
    object setting = SettingsService.GetSetting(key);
    Assert.That(setting, Is.Not.Null);
    Assert.That(setting.GetType(), Is.EqualTo(defaultValue.GetType()));

    object defaultSetting = null!;
    try { defaultSetting = SettingsService.GetDefaultSetting(key); } catch { }
    if (defaultSetting != null)
    {
      Assert.That(defaultSetting.GetType(), Is.EqualTo(defaultValue.GetType()));
      Assert.That(defaultSetting, Is.EqualTo(defaultValue));

      Assert.That(setting.GetType(), Is.EqualTo(defaultSetting.GetType()));
    }
  }
}
