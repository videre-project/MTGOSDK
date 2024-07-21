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

  [TestCase<bool>(Setting.ShowBigCardWindow, true)]
  [TestCase<bool>(Setting.AlwaysShowRedZone, false)]
  [TestCase<bool>(Setting.ShowPhaseLadder, true)]
  [TestCase<bool>(Setting.EnableAnimation, true)]
  [TestCase<bool>(Setting.AutoSizeCards, true)]
  [TestCase<int>(Setting.ManuallyChosenBattlefieldCardSize, 50)]
  [TestCase<double>(Setting.RedZoneDefaultSize, 40.0)]
  [TestCase<bool>(Setting.ShowChatInDuel, false)]
  [TestCase<bool>(Setting.ShowLogInDuel, true)]
  [TestCase<bool>(Setting.IgnoreChatNotifications, false)]
  [TestCase<bool>(Setting.AlwaysDisableBluffing, false)]
  [TestCase<bool>(Setting.AlwaysFastStacking, false)]
  public void Test_UserSettings<TValue>(Setting key, object defaultValue)
    where TValue : notnull
  {
    ValidateSetting(key, defaultValue);
    object setting = SettingsService.GetSetting(key);

    PrimitiveSetting<TValue> entry = SettingsService.UserSettings[key];
    ValidatePrimitiveSetting(key, entry);

    object currentValue = entry.Value;
    Assert.That(currentValue, Is.Not.Null);
    Assert.That(currentValue.GetType(), Is.EqualTo(defaultValue.GetType()));
    Assert.That(currentValue, Is.EqualTo(setting));
  }

  [TestCase<string>(Setting.LastLoginName, "")]
  [TestCase<string>(Setting.LastEULAVersionNumberAgreedTo, "")]
  [TestCase<bool>(Setting.ShowAccountActivationDialog, false)]
  [TestCase<DateTime>(Setting.AgeGateBirthDate)]
  [TestCase<bool>(Setting.JoinBegoneWarning, false)]
  public void Test_ApplicationSettings<TValue>(
    Setting key,
    object? defaultValue = null)
      where TValue : notnull
  {
    // Ensure that the default value is not null (and matches the value type).
    if (defaultValue == null)
      defaultValue = default(TValue)!;

    ValidateSetting(key, defaultValue);
    object setting = SettingsService.GetSetting(key);

    PrimitiveSetting<TValue> entry = SettingsService.ApplicationSettings[key];
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

  public void ValidatePrimitiveSetting<T>(
    Setting key,
    PrimitiveSetting<T> entry)
      where T : notnull
  {
    Assert.That(entry, Is.Not.Null);

    // IPrimitiveSetting wrapper properties
    // Assert.That(entry.IsNull, Is.False);
    Assert.That(entry.Value.GetType(), Is.EqualTo(typeof(T)));

    // ISetting wrapper properties
    Assert.That(entry.Id, Is.EqualTo(key));
    Assert.That(entry.IsLoaded, Is.True);
    Assert.That(entry.IsDefault, Is.False);
    Assert.That(entry.IsReadOnly, Is.False);
    Assert.That(entry.StoreLocally, Is.True);
  }
}
