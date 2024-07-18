/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API;

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
  private static IDictionary UserSettings =>
    Bind<IDictionary>(Unbind(s_settingsService).m_userSettingsStorage);

  /// <summary>
  /// The machine level settings registered with the client.
  /// </summary>
  private static IDictionary ApplicationSettings =>
    Bind<IDictionary>(Unbind(s_settingsService).m_machineSettingsStorage);

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
  public static T GetSetting<T>(string key)
  {
    foreach (var settings in Unbind([UserSettings, ApplicationSettings]))
    {
      for (int i = 0; i < settings.Keys.Count; i++)
      {
        if (settings.Keys[i].ToString() == key)
          return Cast<T>(settings.Values[i].Value);
      }
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
  public static object GetSetting(string key) => GetSetting<object>(key);
}
