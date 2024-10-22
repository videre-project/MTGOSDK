/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.Core.Reflection.DLRWrapper;

// Alias for the settings storage dictionary type.
using SettingsStore = MTGOSDK.Core.Reflection.Proxy.DictionaryProxy<
  MTGOSDK.API.Settings.Setting,
  MTGOSDK.API.Settings.PrimitiveSetting<object>
>;

using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Settings;

/// <summary>
/// Provides access to the MTGO client's user and machine level settings.
/// </summary>
public static class SettingsService
{
  /// <summary>
  /// The MTGO client's settings service instance.
  /// </summary>
  private static readonly ISettings s_settingsService =
    ObjectProvider.Get<ISettings>();

  /// <summary>
  /// The user level settings registered with the client.
  /// </summary>
  /// <remarks>
  /// Reads from the client's <c>user_settings</c> file.
  /// </remarks>
  public static SettingsStore UserSettings =
    new(Unbind(s_settingsService).m_userSettingsStorage);

  /// <summary>
  /// The machine level settings registered with the client.
  /// </summary>
  /// <remarks>
  /// Reads from the client's <c>application_settings</c> file.
  /// </remarks>
  public static SettingsStore ApplicationSettings =
    new(Unbind(s_settingsService).m_machineSettingsStorage);

  /// <summary>
  /// Gets the remote key for the specified setting.
  /// </summary>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The remote key of the setting.</returns>
  /// <remarks>
  /// This method converts the <c>Settings</c> enum to a remote reference to
  /// the client's <c>SettingName</c> enum.
  /// </remarks>
  private static dynamic GetSettingKey(Setting key)
  {
    foreach (var settings in new[] { UserSettings, ApplicationSettings })
    {
      if (settings.TryGetRemoteKey(key, out dynamic remoteKey))
        return remoteKey;
    }

    throw new KeyNotFoundException(
        $"The key '{key}' was not found in the client settings.");
  }

  /// <summary>
  /// Gets the value of the specified application setting from the client.
  /// </summary>
  /// <typeparam name="T">The type of the setting value.</typeparam>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The value of the setting.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown when the specified key is not found in the application settings.
  /// </exception>
  public static T GetSetting<T>(Setting key)
  {
    dynamic remoteKey = GetSettingKey(key);
    var obj = Unbind(s_settingsService).GetSetting(remoteKey)
      ?? throw new KeyNotFoundException(
          $"The key '{key}' was not found in the client settings.");

    return Try(() => Cast<PrimitiveSetting<T>>(obj).Value, default(T));
  }

  /// <summary>
  /// Gets the value of the specified application setting from the client.
  /// </summary>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The value of the setting.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown when the specified key is not found in the application settings.
  /// </exception>
  public static object GetSetting(Setting key) =>
    GetSetting<object>(key);

  /// <summary>
  /// Gets the default value of the specified application setting from the client.
  /// </summary>
  /// <typeparam name="T">The type of the setting value.</typeparam>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The default value of the setting.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown when the specified key is not found in the application settings.
  /// </exception>
  public static T GetDefaultSetting<T>(Setting key)
  {
    dynamic remoteKey = GetSettingKey(key);
    var obj = Unbind(s_settingsService).GetDefaultSetting(remoteKey)
      ?? throw new KeyNotFoundException(
          $"The key '{key}' was not found in the default client settings.");

    return Cast<PrimitiveSetting<T>>(obj).Value;
  }

  /// <summary>
  /// Gets the default value of the specified application setting from the client.
  /// </summary>
  /// <param name="key">The key of the setting to retrieve.</param>
  /// <returns>The default value of the setting.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown when the specified key is not found in the application settings.
  /// </exception>
  public static object GetDefaultSetting(Setting key) =>
    GetDefaultSetting<object>(key);
}
