/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Windows;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Users;
using MTGOSDK.API.Settings;
using MTGOSDK.API.Interface;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Security;
using MTGOSDK.Resources;
using static MTGOSDK.Win32.Constants;

using FlsClient.Interface;
using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model;


namespace MTGOSDK.API;
using static MTGOSDK.API.Events;

/// <summary>
/// Creates a new instance of the MTGO client API.
/// </summary>
public sealed class Client : DLRWrapper<ISession>, IDisposable
{
  /// <summary>
  /// Manages the client's connection and user session information.
  /// </summary>
  private static readonly ISession s_session =
    ObjectProvider.Get<ISession>();

  /// <summary>
  /// Provides basic information about the current user and client session.
  /// </summary>
  private static readonly IFlsClientSession s_flsClientSession =
    ObjectProvider.Get<IFlsClientSession>();

  /// <summary>
  /// View model for the client's main window and scenes.
  /// </summary>
  private static readonly IShellViewModel s_shellViewModel =
    ObjectProvider.Get<IShellViewModel>();

  /// <summary>
  /// View model for the client's login and authentication process.
  /// </summary>
  private static dynamic s_loginManager =>
    Unbind(s_shellViewModel).m_loginViewModel;

  //
  // Static fields and properties
  //

  /// <summary>
  /// The current build version of the running MTGO client.
  /// </summary>
  public static Version Version =>
    new(FileVersionInfo.GetVersionInfo(
      Path.Join(MTGOAppDirectory, "MTGO.exe")
    ).FileVersion);

  /// <summary>
  /// The latest version of the MTGO client that this SDK is compatible with.
  /// </summary>
  /// <remarks>
  /// This version is used to verify that the SDK is compatible with the MTGO
  /// client, using the reference assembly version built into the SDK.
  /// </remarks>
  public static Version CompatibleVersion =
    new(
      EmbeddedResources.GetXMLResource(@"Manifests\MTGO")
        .GetElementsByTagName("assemblyIdentity")[0]
        .Attributes["version"].Value
    );

  //
  // Instance fields and properties
  //

  /// <summary>
  /// Returns the current logged in user's public information.
  /// </summary>
  public static User CurrentUser => new(s_flsClientSession.LoggedInUser.Id);

  /// <summary>
  /// The MTGO client's user session id.
  /// </summary>
  public static Guid SessionId => new(s_flsClientSession.SessionId);

  /// <summary>
  /// Whether the client has started and is currently running.
  /// </summary>
  public static bool HasStarted => RemoteClient.HasStarted;

  /// <summary>
  /// Whether the client is currently online and connected.
  /// </summary>
  public static bool IsConnected => s_flsClientSession.IsConnected;

  /// <summary>
  /// Whether the client is currently logged in.
  /// </summary>
  public static bool IsLoggedIn => s_loginManager.IsLoggedIn;

  /// <summary>
  /// Whether MTGO is currently down for maintenance or is otherwise offline.
  /// </summary>
  public static bool IsUnderMaintenance =>
    Cast<Visibility>(s_loginManager.WarningVisibility) == Visibility.Visible;

  /// <summary>
  /// Whether the client is currently in an interactive session with a user.
  /// </summary>
  public static bool IsInteractive =>
    s_loginManager.IsLoginEnabled == (IsLoggedIn || IsConnected);

  //
  // Constructors and destructors
  //

  /// <summary>
  /// Creates a new instance of the MTGO client API.
  /// </summary>
  /// <param name="options">The client's configuration options.</param>
  /// <param name="loggerFactory">The logger factory to use for logging.</param>
  /// <remarks>
  /// This class is used to manage the client's connection and user session,
  /// and should be instantiated once per application instance and prior to
  /// invoking with other API classes.
  /// </remarks>
  /// <exception cref="ServerOfflineException">
  /// Thrown when the MTGO servers are offline or under maintenance.
  /// </exception>
  /// <exception cref="SetupFailureException">
  /// Thrown when the client process fails to finish installation or start.
  /// </exception>
  /// <exception cref="ExternalErrorException">
  /// Thrown when an external error obstructs the client's connection.
  /// </exception>
  /// <exception cref="VerificationException">
  /// Thrown when the current user session is invalid.
  /// </exception>
  public Client(
    ClientOptions options = default,
    ILoggerProvider? loggerProvider = null,
    ILoggerFactory? loggerFactory = null
  ) : base(
    //
    // This factory delegate will setup the RemoteClient instance before it
    // can be started and connect to the MTGO client in the main constructor.
    //
    factory: async delegate
    {
      // Configures the client's logging options.
      if (loggerProvider != null) Log.SetProviderInstance(loggerProvider);
      if (loggerFactory != null) Log.SetFactoryInstance(loggerFactory);
      Log.Debug("Running the MTGO client API factory.");

      // Sets the client's disposal policy.
      if(options.CloseOnExit)
        RemoteClient.CloseOnExit = true;

      // Starts a new MTGO client process.
      if (options.CreateProcess)
      {
        // Close any existing MTGO processes.
        if (!await WaitUntil(() => !RemoteClient.KillProcess(), delay: 10))
          throw new SetupFailureException("Unable to close existing MTGO processes.");

        // Start a new MTGO process.
        await RemoteClient.StartProcess();
      }
    })
  {
    // Ensure that the SDK's reference types are compatible with MTGO.
    if (ValidateVersion(assert: false))
      Log.Debug("The SDK's reference types match MTGO v{Version}.", Version);

    // Initializes the client connection and starts the MTGO client API.
    RemoteClient.EnsureInitialize();
    Log.Information("Initialized the MTGO client API.");

    // Minimize the MTGO window after startup.
    if (options.StartMinimized)
      RemoteClient.MinimizeWindow();

    //
    // Closes any blocking dialogs preventing the client from logging in.
    //
    // This will often check for the presence of certain classes and UI elements
    // that may still be initializing, so for consistency we give our initial
    // checks a few seconds to complete before proceeding.
    //
    if (options.AcceptEULAPrompt && !Retry(() => IsConnected, delay: 1000))
    {
      // Check if the last accepted EULA version is still the latest version.
      var EULAVersion = Retry(() =>
        SettingsService.GetSetting<Version>(Setting.LastEULAVersionNumberAgreedTo),
        delay: 1000, retries: 5);
      if (Version > EULAVersion)
      {
        Log.Debug("Accepting EULA prompt for MTGO v{Version}.", Version);
        SettingsService.SetSetting(
          Setting.LastEULAVersionNumberAgreedTo,
          Version.ToString()
        );
        SettingsService.Save();

        // Restart the MTGO process with the updated EULA version.
        Log.Debug("Restarting the MTGO process with the updated EULA version.");
        RemoteClient.Dispose();
        Task.Run(async () =>
        {
          // Close any existing MTGO processes.
          if (!await WaitUntil(() => !RemoteClient.KillProcess(), delay: 10))
            throw new SetupFailureException("Unable to restart MTGO.");

          // Start a new MTGO process.
          await RemoteClient.StartProcess();
        }).Wait();
        RemoteClient.EnsureInitialize();
        Log.Debug("Restarted the MTGO process with the updated EULA version.");
      }
    }

    // Verify that MTGO is not under maintenance or is otherwise offline.
    if (IsUnderMaintenance)
      throw new ServerOfflineException("MTGO is currently under maintenance.");

    // Verify that any existing user sessions are valid.
    if ((SessionId == Guid.Empty) && IsConnected)
      throw new VerificationException("Current user session is invalid.");
  }

  /// <summary>
  /// Checks if the MTGO servers are online.
  /// </summary>
  /// <returns>True if the servers are online, false otherwise.</returns>
  /// <exception cref="HttpRequestException">
  /// Thrown when the request to fetch server status fails.
  /// </exception>
  /// <exception cref="ExternalErrorException">
  /// Thrown when no MTGO servers are found in the response.
  /// </exception>
  public static async Task<bool> IsOnline()
  {
    using (HttpClient client = new HttpClient())
    {
      string url = "https://census.daybreakgames.com/s:dgc/get/global/game_server_status?game_code=mtgo&c:limit=1000";
      using var response = await client.GetAsync(url);

      if (!response.IsSuccessStatusCode)
        throw new HttpRequestException("Failed to fetch server status");

      using var content = response.Content;
      var json = JObject.Parse(await content.ReadAsStringAsync());

      if (json["returned"].ToObject<int>() == 0)
        throw new ExternalErrorException("No MTGO servers were found");

      // Check if any servers are online.
      string[] statuses = [ "high", "medium", "low" ];
      return json["game_server_status_list"].Any(s =>
          statuses.Contains(s["last_reported_state"].ToObject<string>()));
    }
  }

  /// <summary>
  /// Checks if the MTGO login server is available.
  /// </summary>
  /// <returns>True if the login server is available, false otherwise.</returns>
  /// <exception cref="HttpRequestException">
  /// Thrown when the request to fetch server status fails.
  /// </exception>
  public static async Task<bool> IsLoginAvailable()
  {
    using (HttpClient client = new())
    {
      string url = "https://auth.daybreakgames.com/api/mtgostatus";
      using var response = await client.GetAsync(url);

      if (!response.IsSuccessStatusCode)
        throw new HttpRequestException("Failed to fetch server status");

      using var content = response.Content;
      var json = JObject.Parse(await content.ReadAsStringAsync());

      if (!json["response"].ToObject<string>().Equals("success"))
        throw new HttpRequestException("Failed to fetch server status");

      // Check if the login server is up.
      return json["message"].ToObject<string>() == "UP";
    }
  }

  /// <summary>
  /// Verifies the client's compatibility with the SDK version.
  /// </summary>
  /// <param name="assert">Whether to throw an exception on failure.</param>
  /// <returns>Whether the client version is compatible.</returns>
  /// <exception cref="ValidationException">
  /// Thrown when the client version is not compatible with the SDK.
  /// </exception>
  [ExcludeFromCodeCoverage]
  public static bool ValidateVersion(bool assert = false)
  {
    // Verify that an existing MTGO installation is present.
    if (!File.Exists(AppRefPath) || Try(() => MTGOAppDirectory) == null)
    {
      if (assert)
        throw new ValidationException("The MTGO client is not installed.");

      return true; // Otherwise assume that a new MTGO version will be installed.
    }

    if (Version < CompatibleVersion)
      Log.Warning("The MTGO version {Version} does not match the SDK's compatible version {CompatibleVersion} and may no longer function correctly.",
        Version, CompatibleVersion);
    else if (Version > CompatibleVersion)
      Log.Warning("The MTGO version {Version} is newer than the SDK's compatible version {CompatibleVersion} and may not function correctly.",
        Version, CompatibleVersion);
    else return true;

    if (assert)
      throw new ValidationException(
        "The MTGO client version is not compatible with the SDK.");

    return false;
  }

  /// <summary>
  /// Cleans up any cached remote objects pinning objects in client memory.
  /// </summary>
  public void ClearCaches()
  {
    Log.Debug("Disposing all pinned remote objects registered with the client.");
    CollectionManager.Cards.Clear();
    ObjectProvider.ResetCache();
  }

  /// <summary>
  /// Disposes of the remote client handle.
  /// </summary>
  public void Dispose() => RemoteClient.Dispose();

  //
  // ISession wrapper methods
  //

  /// <summary>
  /// Waits until the client has connected and is ready to be interacted with.
  /// </summary>
  /// <returns>Whether the client is ready.</returns>
  /// <remarks>
  /// The client may take a few seconds to close the overlay when done loading.
  /// </remarks>
  public async Task<bool> WaitForClientReady() =>
    await WaitUntil(() =>
      // Checks to see if the ShellViewModel has finished initializing.
      Unbind(s_shellViewModel).IsSessionConnected == true &&
      Unbind(s_shellViewModel).ShowLoadDeckSplashScreen == false &&
      Unbind(s_shellViewModel).m_blockingProgressInstances.Count == 0 &&
      // Checks to see if the HomeSceneViewModel has finished initializing.
      Unbind(s_shellViewModel.CurrentScene).FeaturedTournaments.Count > 0 &&
      Unbind(s_shellViewModel.CurrentScene).SuggestedLeagues.Count > 0 &&
      Unbind(s_shellViewModel.CurrentScene).JoinedEvents.Count >= 0,
      delay: 500, // in ms
      retries: 60 // or 30 seconds
    );

  /// <summary>
  /// Creates a new user session and connects MTGO to the main server.
  /// </summary>
  /// <param name="username">The user's login name.</param>
  /// <param name="password">The user's login password.</param>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the client is already logged in.
  /// </exception>
  /// <exception cref="ServerOfflineException">
  /// Thrown when the login server is currently offline.
  /// </exception>
  /// <exception cref="ArgumentException">
  /// Thrown when the user's credentials are missing or malformed.
  /// </exception>
  /// <exception cref="TimeoutException">
  /// Thrown when the client times out trying to connect and initialize.
  /// </exception>
  public async Task LogOn(string username, SecureString password)
  {
    if (IsLoggedIn)
      throw new InvalidOperationException("Cannot log on while logged in.");

    if (!await RetryAsync(IsLoginAvailable, retries: 3) && !await IsOnline())
      throw new ServerOfflineException("The login server is currently offline.");

    // Passes the user's credentials to the MTGO client for authentication.
    s_loginManager.ScreenName = username;
    s_loginManager.Password = password.RemoteSecureString();
    if (!s_loginManager.LogOnCanExecute())
    {
      s_loginManager.Password = null;
      throw new ArgumentException("Missing one or more user credentials.");
    }

    // Executes the login command and creates a new task to connect the client.
    Log.Debug("Logging in as {Username}.", username);
    s_loginManager.LogOnExecute();
    if (!await WaitForClientReady() && !s_loginManager.IsLoggedIn)
      throw new TimeoutException("Failed to connect and initialize the client.");

    // Explicitly update state for a non-interactive session.
    s_loginManager.IsLoggedIn = true;
    s_loginManager.IsLoginEnabled = false;
  }

  /// <summary>
  /// Closes the current user session and returns to the login screen.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the client is not currently logged in.
  /// </exception>
  /// <exception cref="TimeoutException">
  /// Thrown when the client times out trying to disconnect.
  /// </exception>
  public async Task LogOff()
  {
    if (!(IsLoggedIn || IsConnected))
      throw new InvalidOperationException("Cannot log off while disconnected.");

    if (IsInteractive)
      throw new InvalidOperationException("Cannot log off an interactive session.");

    // Invokes logoff command and disconnects the MTGO client.
    Log.Debug("Logging off and disconnecting the client.");
    s_loginManager.Disconnect();
    if (!await WaitUntil(() => !IsConnected || !RemoteClient.IsInitialized )) {
      throw new TimeoutException("Failed to log off and disconnect the client.");
    }
  }

  //
  // ISession wrapper events
  //

  /// <summary>
  /// Occurs when a new system alert or warning is displayed on the MTGO client.
  /// </summary>
  /// <remarks>
  /// This event is raised when the client displays a 'Warning' modal window.
  /// </remarks>
  public EventProxy<SystemAlertEventArgs> SystemAlertReceived =
    new(/* ISession */ s_session, nameof(SystemAlertReceived));

  /// <summary>
  /// Occurs when a login attempt failed due to connection or authentication.
  /// </summary>
  /// <remarks>
  /// This may also occur when a login attempt requires 2-factor authentication,
  /// requesting a challenge code to finish logging in.
  /// </remarks>
  public EventProxy<ErrorEventArgs> LogOnFailed =
    new(/* ISession */ s_session, nameof(LogOnFailed));

  /// <summary>
  /// Occurs when a connection exception is thrown by the MTGO client.
  /// </summary>
  /// <remarks>
  /// This can occur when login fails or when disconnected from the server.
  /// </remarks>
  public EventProxy<ErrorEventArgs> ErrorReceived =
    new(/* ISession */ s_session, nameof(ErrorReceived));

  /// <summary>
  /// Occurs when the client's connection status changes.
  /// </summary>
  public EventProxy IsConnectedChanged =
    new(/* ISession */ s_session, nameof(IsConnectedChanged));
}
