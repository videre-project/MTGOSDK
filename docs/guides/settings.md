# Settings Guide

This guide covers the Settings APIs for reading MTGO client configuration and preferences. These APIs provide read-only access to user preferences, window layouts, and client state.

## Overview

The Settings APIs let you read MTGO's configuration values, including UI preferences, login information, window positions, and feature flags. This is useful for:

- **Diagnostics**: Understanding a user's configuration when troubleshooting issues
- **Overlay alignment**: Reading window sizes and positions to align external UI with MTGO
- **Feature detection**: Checking which optional features are enabled
- **Configuration export**: Backing up user preferences for documentation or support

Settings are stored locally on the user's machine and persist across sessions. The SDK provides read access to these values. Modifying settings requires using MTGO's settings UI directly.

```csharp
using MTGOSDK.API.Settings;
```

---

## Reading Settings

Settings are accessed by key using `GetSetting`. The `Setting` enum defines all available setting keys:

```csharp
var lastUser = SettingsService.GetSetting(Setting.LastLoginName);
Console.WriteLine($"Last login: {lastUser}");
```

This returns the value as an object, which works for quick inspection but requires casting if you need to use the value in typed code. For strongly-typed access, use the generic overload:

```csharp
bool showBigCard = SettingsService.GetSetting<bool>(Setting.ShowBigCardWindow);
Console.WriteLine($"Show big card: {showBigCard}");
```

The generic version handles the type conversion for you. If the setting doesn't exist or can't be converted to the requested type, you'll get an exception. Use this when you know the expected type of a setting.

The `Setting` enum includes many configuration keys covering UI layout, gameplay preferences, and client behavior. Common settings include:

- `LastLoginName`: The username from the most recent login
- `ShowBigCardWindow`: Whether the large card preview window is enabled
- `ShowPhaseLadder`: Whether the phase ladder (turn structure display) is visible
- Various scene-specific settings for window positions and sizes

### Default Values

You can also retrieve the default value for a setting, which represents what MTGO uses before the user customizes it:

```csharp
bool defaultPhaseLadder = SettingsService.GetDefaultSetting<bool>(Setting.ShowPhaseLadder);
Console.WriteLine($"Phase ladder default: {defaultPhaseLadder}");
```

Comparing the current value against the default can help you identify which settings the user has customized. This is useful for support diagnostics (finding non-default configurations that might cause issues) or for building a "reset to defaults" feature in your own settings UI.

---

## Common Use Cases

### Diagnostics and Support

When troubleshooting user issues, read their current settings to understand their configuration:

```csharp
var settings = new Dictionary<string, object>
{
  ["ShowBigCard"] = SettingsService.GetSetting<bool>(Setting.ShowBigCardWindow),
  ["ShowPhaseLadder"] = SettingsService.GetSetting<bool>(Setting.ShowPhaseLadder),
  ["LastUser"] = SettingsService.GetSetting(Setting.LastLoginName),
};

// Export or display for debugging
```

This helps identify if a problem is related to a specific setting or if the user has an unusual configuration.

### Overlay Alignment

The DuelScene settings include window sizes and positions that define where MTGO's game UI elements appear. If you're building an overlay that needs to align with MTGO's game view (like a life counter or deck tracker), these settings tell you where UI elements are positioned:

```csharp
// Read DuelScene layout settings for overlay positioning
// Exact setting keys depend on your overlay needs
```

Reading these values lets your overlay adapt to the user's layout preferences rather than assuming fixed positions.

---

## Next Steps

- [Interface Guide](./interface.md) - Displaying notifications and dialogs
- [Getting Started](../getting-started.md) - Installation and setup
