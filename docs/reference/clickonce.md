# ClickOnce Deployment

This document explains how MTGO uses ClickOnce deployment, how the SDK discovers and launches MTGO installations, and considerations for CI/CD environments.

## Overview

MTGO is deployed using Microsoft's ClickOnce technology. ClickOnce is a deployment mechanism introduced in .NET Framework 2.0 that allows Windows applications to be installed and updated from a web server or network share without requiring administrative privileges.

### Why ClickOnce?

Traditional Windows installers (MSI/EXE) require administrative access and install applications system-wide. ClickOnce takes a different approach:

| Traditional Installer | ClickOnce |
|----------------------|-----------|
| Requires admin rights | Per-user, no elevation needed |
| System-wide installation | Isolated in user's AppData |
| Manual update process | Automatic update checks |
| Single version installed | Side-by-side versions supported |
| Shared DLLs (DLL hell) | Isolated assemblies per app |

For MTGO, this means players can install and update the client without IT department involvement, and updates happen automatically when launching the application. The SDK needs to understand these internals to locate MTGO's installation directory, handle fresh installations, and launch the application correctly across different environments.

---

## Filesystem Structure

ClickOnce creates an application cache in the user's local application data folder. Unlike traditional installers that place executables in `C:\Program Files`, ClickOnce maintains a complex directory structure designed for isolation and versioning.

### Cache Location

```
%LocalAppData%\Apps\2.0\
```

The "2.0" refers to the ClickOnce deployment version, not the .NET Framework version. This folder exists for all users who have ever launched a ClickOnce application.

### Directory Layout

```
Apps\2.0\
├── {component_token}\                    # Random 8-char token
│   └── {obfuscated_folder}\              # Another random segment
│       └── {obfuscated_folder}\
│           └── mtgo..tion_{hash}\        # Truncated app name + version hash
│               ├── MTGO.exe              # Main executable
│               ├── *.dll                 # Application assemblies
│               ├── MTGO.exe.manifest     # Application manifest (XML)
│               └── MTGO.exe.config       # App configuration
│
└── Data\
    └── {state_token}\
        └── {obfuscated}\
            └── mtgo..tion_{hash}\
                └── Data\                 # Isolated data directory
                    └── (user files)
```

### Why Obfuscated Names?

The random folder names serve several purposes:

1. **Isolation**: Different applications can't accidentally share folders
2. **Security**: Makes it harder to predict or target installation paths
3. **Versioning**: Each version gets a unique hash-based folder name
4. **No conflicts**: Multiple users on the same machine get unique caches

The folder name `mtgo..tion_{hash}` is a truncated version of the full application name ("Magic The Gathering Online.application"), with the middle portion replaced by ".." and a version/hash suffix.

### Version Retention

ClickOnce retains only two versions at a time:
- The currently running version
- The immediately previous version (for rollback)

When a new update is installed, the oldest version is deleted. This keeps disk usage bounded while allowing recovery from a bad update.

### Cache Quota

Online-only ClickOnce applications (those that run directly from a URL without installation) are subject to a 250MB cache quota. Installed applications like MTGO are exempt from this limit.

To manually clear the ClickOnce cache:
```powershell
rundll32 dfshim.dll CleanOnlineAppCache
```

---

## Registry Structure

ClickOnce uses the Windows Registry to track installed applications and their locations. The SDK reads these registry keys to discover where MTGO is installed without hardcoding paths.

### SideBySide Registry Path

All ClickOnce deployment data lives under:

```
HKEY_CURRENT_USER\SOFTWARE\Classes\Software\Microsoft\Windows\CurrentVersion\Deployment\
```

The "SideBySide" subkey manages per-user application installations:

```
...\Deployment\SideBySide\2.0\
├── ComponentStore_RandomString = "abc123xy"     # Token for app cache
│
└── StateManager\
    ├── StateStore_RandomString = "def456gh"     # Token for data cache
    │
    └── Applications\
        └── mtgo..tion_abc123...\                # Per-app metadata
            ├── (version info)
            └── (deployment state)
```

### What Are These Random Tokens?

The `ComponentStore_RandomString` and `StateStore_RandomString` values are 8-character tokens that form part of the filesystem path. They're generated when ClickOnce first initializes on a machine.

**ComponentStore_RandomString**: Links to the application executable cache
```
Apps\2.0\{ComponentStore_RandomString}\...
```

**StateStore_RandomString**: Links to the isolated data storage
```
Apps\2.0\Data\{StateStore_RandomString}\...
```

These tokens can change if:
- ClickOnce is reset or corrupted
- Certain Windows updates modify deployment settings
- The user clears the ClickOnce cache

When the tokens change, applications appear to need reinstallation because ClickOnce can no longer locate their cached files.

### How the SDK Uses Registry

The `ClickOncePaths` class reads these registry values to construct paths dynamically:

```csharp
private const string SIDEBYSIDE_REGISTRY_KEY_PATH =
  @"SOFTWARE\Classes\Software\Microsoft\Windows\CurrentVersion\Deployment\SideBySide\2.0";

public static string? ApplicationDirectory
{
  get
  {
    // Read the ComponentStore token from registry
    var token = RegistryStore.GetRegistryToken(
      SIDEBYSIDE_REGISTRY_KEY_PATH,
      "ComponentStore_RandomString");
      
    if (token == null) return null;  // MTGO never installed
    
    // Construct the cache path
    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      @"Apps\2.0",
      token);
  }
}
```

Once the SDK has the base cache path, it uses a glob pattern to find MTGO's specific folder:

```csharp
// Constants.cs
public static string? MTGOAppDirectory =>
  ClickOncePaths.ApplicationDirectory is string appDir
    ? new Glob(@$"{appDir}\mtgo..tion_*")  // Match any version
    : null;
```

The glob pattern `mtgo..tion_*` matches any folder starting with the truncated application name, regardless of version. This means the SDK automatically finds the latest installed version without hardcoding version numbers.

---

## dfsvc Service

The ClickOnce Deployment Service (`dfsvc.exe`) is the Windows component that handles all ClickOnce operations.

### Location and Purpose

```
C:\Windows\Microsoft.NET\Framework\v4.0.30319\dfsvc.exe
```

When a user clicks an `.application` file (deployment manifest) or `.appref-ms` file (application reference), Windows invokes dfsvc to:

1. **Parse the deployment manifest**: Read the XML file to determine application identity, version, and update location
2. **Check for updates**: Compare the manifest version against the cached version
3. **Download updates**: If a newer version exists, download the new files
4. **Verify signatures**: Validate code signing certificates if present
5. **Grant permissions**: Apply the trust level specified in the manifest
6. **Launch the application**: Start the main executable

### Lifecycle Behavior

After launching an application, dfsvc doesn't exit immediately. Instead, it stays running for approximately 15 minutes. This optimization serves two purposes:

1. **Quick relaunches**: If the user closes and reopens the app, dfsvc is already warm
2. **Update checks**: Can perform background update checks during this window

You can observe this by checking Task Manager after launching MTGO. The dfsvc.exe process will remain even after MTGO is closed.

### The CI/CD Problem

On consumer Windows installations, dfsvc is registered as a file association handler and runs automatically when needed. However, on Windows Server editions (including GitHub Actions runners), dfsvc may not be running because:

1. No user has ever launched a ClickOnce app in this session
2. The Desktop Experience feature may not be installed
3. Server Core installations have minimal UI components

The SDK detects this and starts dfsvc manually before attempting to launch MTGO:

```csharp
// RemoteClient.cs - InstallOrUpdate()

// Check if dfsvc is running
if (Process.GetProcessesByName("dfsvc").Length == 0)
{
  Log.Debug("The ClickOnce service is not running. Starting it now.");
  
  // Start dfsvc without waiting for it to exit
  await StartShellProcess(
    ClickOncePaths.ClickOnceServiceExecutable,
    timeout: TimeSpan.Zero  // Fire and forget
  );
}
```

The `timeout: TimeSpan.Zero` is important since dfsvc stays running indefinitely once started. We just need to ensure it's running before proceeding with the installation.

---

## Launcher.exe

The SDK includes a separate launcher application for programmatic ClickOnce installations. This approach enables headless MTGO installation without user prompts.

### Why a Separate Launcher?

The .NET Framework provides `System.Deployment.Application.InPlaceHostingManager`, a class specifically designed for programmatic ClickOnce installation. However, this API has a critical limitation: **it only exists in .NET Framework 4.x**.

```csharp
// This namespace doesn't exist in .NET Core/.NET 5+
using System.Deployment.Application;
```

Microsoft never ported the ClickOnce hosting APIs to modern .NET. Since the SDK targets .NET 10, it cannot directly reference these APIs. The solution is a separate launcher executable:

| Component | Target Framework | Purpose |
|-----------|------------------|---------|
| MTGOSDK | .NET 10 | Main SDK library |
| Launcher.exe | .NET Framework 4.8 | ClickOnce installation bridge |

The launcher is compiled as a .NET Framework 4.8 application and embedded as a binary resource in the SDK assembly.

### InPlaceHostingManager API

The launcher uses `InPlaceHostingManager` to programmatically install ClickOnce applications:

```csharp
// Launcher/Program.cs
public static void Main(string[] args)
{
  string manifestUri = args[0];  // e.g., "http://mtgo.patch.daybreakgames.com/..."
  
  InPlaceHostingManager iphm = new(new Uri(manifestUri), false);
  AutoResetEvent waitHandle = new(false);

  // Step 1: Download and parse the deployment manifest
  iphm.GetManifestCompleted += (sender, e) =>
  {
    if (e.Error != null)
      throw new Exception("Could not download manifest: " + e.Error.Message);
    waitHandle.Set();
  };
  iphm.GetManifestAsync();
  waitHandle.WaitOne();

  // Step 2: Assert that we trust this application
  // The 'true' parameter grants full trust (required for MTGO)
  iphm.AssertApplicationRequirements(true);

  // Step 3: Download all application files
  iphm.DownloadApplicationCompleted += (sender, e) =>
  {
    if (e.Error != null)
      throw new Exception("Could not download application: " + e.Error.Message);
    waitHandle.Set();
  };
  iphm.DownloadApplicationAsync();
  waitHandle.WaitOne();

  Environment.Exit(0);
}
```

The process is asynchronous because ClickOnce downloads can take several minutes for large applications like MTGO.

### SDK Integration

The SDK extracts and invokes the launcher at runtime:

```csharp
// RemoteClient.cs - InstallOrUpdate()
public static async Task InstallOrUpdate()
{
  // Extract Launcher.exe from embedded resources
  byte[] launcherResource = GetBinaryResource(@"Resources\Launcher.exe");
  string launcherPath = Path.Combine(Bootstrapper.AppDataDir, "Launcher.exe");
  
  // Only write if the file doesn't exist or has changed
  OverrideFileIfChanged(launcherPath, launcherResource);

  // Run the launcher with MTGO's deployment manifest URL
  await StartShellProcess(
    launcherPath,
    ApplicationUri,  // "http://mtgo.patch.daybreakgames.com/..."
    timeout: TimeSpan.FromMinutes(5)
  );
}
```

The 5-minute timeout accounts for slow network connections or large updates. If the launcher takes longer, the SDK assumes something went wrong.

---

## Process Launch

After installation, launching MTGO requires more than just starting an executable. The SDK must handle Windows session management and privilege requirements.

### The .appref-ms Shortcut

ClickOnce creates a Start Menu shortcut for installed applications:

```csharp
public static string AppRefPath =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
    "Daybreak Game Company LLC",
    "Magic The Gathering Online .appref-ms"
  );
```

The `.appref-ms` file is a special shortcut format that tells dfsvc which application to launch. Opening this file triggers dfsvc to:
1. Check for updates
2. Apply any pending updates
3. Launch the application

### Session 0 Isolation

Windows Vista and later implement "Session 0 isolation" as a security measure. Understanding this is critical for CI/CD scenarios.

**What is Session 0?**

Windows runs processes in isolated sessions:
- **Session 0**: Reserved for services and system processes. Has no interactive desktop.
- **Session 1+**: User login sessions. Each has its own desktop, clipboard, etc.

Before Vista, services could display UI on the user's desktop (Session 0 wasn't isolated). This was a security risk, so Microsoft separated them.

**Impact on MTGO**

MTGO is a WPF application that requires:
- A visible window
- Message pump for UI events
- Access to graphics hardware for rendering

None of these work in Session 0. If you try to launch MTGO from a Windows service or a CI runner executing in Session 0, it will fail silently or crash.

### RunAsDesktopUser

The SDK uses `CreateProcessAsUser` to launch MTGO in the logged-in user's desktop session:

```csharp
// RemoteClient.cs - StartProcess()
try
{
  // Launch in the user's desktop session, not our current session
  using var process = ProcessUtilities.RunAsDesktopUser(AppRefPath, "");
  await process.WaitForExitAsync();
}
catch
{
  // Fall back to shell execution if privileges aren't available
  await StartShellProcess(AppRefPath, timeout: TimeSpan.FromSeconds(10));
}
```

The `RunAsDesktopUser` helper:
1. Gets the currently logged-in user's token using `WTSQueryUserToken`
2. Calls `CreateProcessAsUser` with that token
3. Specifies `winsta0\default` as the desktop

This requires specific Windows privileges:

| Privilege | Constant | Purpose |
|-----------|----------|---------|
| Increase quotas | `SeIncreaseQuotaPrivilege` | Assign memory quotas to the new process |
| Replace token | `SE_ASSIGNPRIMARYTOKEN_NAME` | Assign a primary token to a process |

Services running as LocalSystem typically have these privileges. Regular user accounts don't.

---

## CI/CD Considerations

Running MTGO in automated environments requires addressing several Windows security and session management challenges.

### The Session 0 Problem

GitHub Actions runners and most CI systems run as Windows services, which means they execute in Session 0. MTGO cannot run in Session 0.

**Detection:**
```csharp
if (!Environment.UserInteractive)
{
  throw new ExternalErrorException(
    "Could not launch MTGO in user interactive mode.");
}
```

**Solutions:**

| Approach | How It Works | Pros | Cons |
|----------|--------------|------|------|
| RDP session | Connect via Remote Desktop before running tests | Creates interactive session | Requires active connection |
| Auto-logon | Configure Windows to log in a user at boot | Persistent session | Security risk |
| `tscon` | Reconnect a disconnected session | Works with auto-logon | Complex setup |
| Virtual display | Use a virtual display driver | Works in truly headless environments | Limited MTGO compatibility |

For most CI scenarios, configuring auto-logon with a dedicated test user account is the recommended approach.

### dfsvc Availability

As discussed in the dfsvc section, Windows Server editions may not have dfsvc running. The SDK handles this automatically, but initial startup will be slower as dfsvc initializes.

### Privilege Configuration

If your CI runner needs to use `CreateProcessAsUser`, configure these privileges via Group Policy:

1. Open `gpedit.msc`
2. Navigate to: Computer Configuration → Windows Settings → Security Settings → Local Policies → User Rights Assignment
3. Add your CI runner account to:
   - "Replace a process level token"
   - "Adjust memory quotas for a process"

For domain-joined machines, this can be pushed via domain Group Policy.

---

## Troubleshooting

### MTGO Not Found

**Symptom:** `MTGOAppDirectory` returns null

**Causes:**
1. MTGO has never been installed on this machine
2. Registry tokens are missing or corrupted
3. ClickOnce cache was cleared

**Solution:**
```csharp
if (MTGOAppDirectory == null)
{
  await RemoteClient.InstallOrUpdate();
}
```

You can manually verify the registry:
```powershell
Get-ItemProperty "HKCU:\SOFTWARE\Classes\Software\Microsoft\Windows\CurrentVersion\Deployment\SideBySide\2.0" 
```

### Launch Fails Silently

**Symptom:** StartProcess returns but MTGO never appears

**Diagnosis:**
```powershell
# Check if dfsvc is running
Get-Process dfsvc -ErrorAction SilentlyContinue

# Check for recent ClickOnce errors in Event Viewer
Get-EventLog -LogName Application -Source "*ClickOnce*" -Newest 10
```

**Solution:** Ensure dfsvc is running via `InstallOrUpdate()`.

### Session 0 Errors

**Symptom:** "Could not launch MTGO in user interactive mode"

**Diagnosis:** Check `Environment.UserInteractive` - if false, you're in Session 0.

**Solutions:**
- Run from an interactive session
- Configure auto-logon
- Use RDP to create a session before running tests

---

## See Also

- [Remote Client](../architecture/remote-client.md) - Process management and IPC
- [Client Reference](./client.md) - Client initialization options
- [Connection Lifecycle](./connection-lifecycle.md) - Reconnection patterns
