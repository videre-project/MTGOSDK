/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Settings;

/// <summary>
/// Provides access to the MTGO client's user and machine level settings.
/// </summary>
public static class SettingsService
{
  /// <summary>
  /// Verifies that SDK's type enums match the client's settings enums.
  /// </summary>
  static SettingsService() =>
    TypeValidator.ValidateEnums<Setting, SettingName>();

  /// <summary>
  /// The MTGO client's settings service instance.
  /// </summary>
  private static readonly ISettings s_settingsService =
    ObjectProvider.Get<ISettings>();

  /// <summary>
  /// The user level settings registered with the client.
  /// </summary>
  public static DictionaryProxy<Setting, object> UserSettings =>
    new(Unbind(s_settingsService).m_userSettingsStorage);

  /// <summary>
  /// The machine level settings registered with the client.
  /// </summary>
  public static DictionaryProxy<Setting, object> ApplicationSettings =>
    new(Unbind(s_settingsService).m_machineSettingsStorage);

  /// <summary>
  /// Gets the value of the specified application setting from the client.
  /// </summary>
  /// <remarks>
  /// Application settings correspond to entries in the <c>SettingName</c> enum.
  /// </remarks>
  /// <typeparam name="T">The type of the setting value.</typeparam>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The value of the setting.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown when the specified key is not found in the application settings.
  /// </exception>
  public static T GetSetting<T>(Setting key)
  {
    foreach (var settings in new[] { UserSettings, ApplicationSettings })
    {
      if (settings.TryGetValue(key, out dynamic entry))
        return Cast<T>(entry.Value);
    }

    throw new KeyNotFoundException(
        $"The key '{key}' was not found in the application settings.");
  }

  /// <summary>
  /// Gets the value of the specified application setting from the client.
  /// </summary>
  /// <remarks>
  /// Application settings correspond to entries in the <c>SettingName</c> enum.
  /// </remarks>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The value of the setting.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown when the specified key is not found in the application settings.
  /// </exception>
  public static object GetSetting(Setting key) => GetSetting<object>(key);
}
