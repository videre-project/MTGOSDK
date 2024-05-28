/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Extensions.Logging;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Users;
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
  //
  // Static fields and properties
  //

  /// <summary>
  /// Manages the client's connection and user session information.
  /// </summary>
  private static readonly ISession s_session =
    Defer(ObjectProvider.Get<ISession>);

  /// <summary>
  /// Provides basic information about the current user and client session.
  /// </summary>
  private static readonly IFlsClientSession s_flsClientSession =
    Defer(ObjectProvider.Get<IFlsClientSession>);

  /// <summary>
  /// View model for the client's main window and scenes.
  /// </summary>
  private static dynamic s_shellViewModel =>
    ObjectProvider.Get<IShellViewModel>(bindTypes: false);

  /// <summary>
  /// View model for the client's login and authentication process.
  /// </summary>
  private static ILoginViewModel s_loginManager =>
    ObjectProvider.Get<ILoginViewModel>();

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
  /// Internal reference to the current logged in user.
  /// </summary>
  private static User? m_currentUser;

  /// <summary>
  /// Returns the current logged in user's public information.
  /// </summary>
  public static User CurrentUser
  {
    get
    {
      // Only fetch and update the current user if the user Id has changed.
      int userId = s_flsClientSession.LoggedInUser.Id;
      if (userId != m_currentUser?.Id) {
        string username = s_flsClientSession.LoggedInUser.Name;
        m_currentUser = new User(userId, username);
      }

      return m_currentUser;
    }
  }

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
  /// Whether the client is currently in an interactive session with a user.
  /// </summary>
  public static bool IsInteractive =>
    Unbind(s_loginManager).IsLoginEnabled == (IsLoggedIn || IsConnected);

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
  /// <exception cref="SetupFailedException">
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
    ILoggerFactory? loggerFactory = null
  ) : base(
    //
    // This factory delegate will setup the RemoteClient instance before it
    // can be started and connect to the MTGO client in the main constructor.
    //
    factory: async delegate
    {
      // Configures the client's logging options.
      if (loggerFactory != null) Log.SetFactoryInstance(loggerFactory);
      Log.Debug("Running the MTGO client API factory.");

      // Starts a new MTGO client process.
      if (options.CreateProcess && !(await RemoteClient.StartProcess()))
        throw new SetupFailedException("Failed to start the MTGO process.");

      // Sets the client's disposal policy.
      if(options.DestroyOnExit)
        RemoteClient.DestroyOnExit = true;

      // Configure the remote client connection.
      if(options.Port != null)
        RemoteClient.Port = Cast<ushort>(options.Port);

      // Verifies the client's compatibility with the SDK version.
      if (Version < CompatibleVersion)
        Log.Warning("The MTGO version {Version} does not match the SDK's compatible version {CompatibleVersion} and may no longer function correctly.",
          Version, CompatibleVersion);
      else if (Version > CompatibleVersion)
        Log.Warning("The MTGO version {Version} is newer than the SDK's compatible version {CompatibleVersion} and may not function correctly.",
          Version, CompatibleVersion);
    })
  {
    //
    // Ensure all deferred static fields in the queue are initialized.
    //
    // This will initialize the connection to the MTGO client and load all
    // static fields in this class that depend on the initialization of any
    // remote objects.
    //
    Log.Trace("Constructing deferred static fields for {Type}.", typeof(Client));
    Construct(_ref: s_flsClientSession /* Can be any deferred instance */);
    Log.Information("Initialized the MTGO client API.");

    // Minimize the MTGO window after startup.
    if (options.StartMinimized)
      RemoteClient.MinimizeWindow();

    // Verify that any existing user sessions are valid.
    if ((SessionId == Guid.Empty) && IsConnected)
      throw new VerificationException("Current user session is invalid.");

    // Closes any blocking dialogs preventing the client from logging in.
    if (options.AcceptEULAPrompt && !IsConnected)
      WindowUtilities.CloseDialogs();
  }

  /// <summary>
  /// Cleans up any cached remote objects pinning objects in client memory.
  /// </summary>
  /// <param name="disconnect">Whether to disconnect the client.</param>
  public void ClearCaches(bool disconnect = false)
  {
    if (disconnect) RemoteClient.Dispose();

    Log.Debug("Disposing all pinned remote objects registered with the client.");
    UserManager.Users.Clear();
    CollectionManager.Cards.Clear();
  }

  /// <summary>
  /// Disposes of the remote client handle.
  /// </summary>
  public void Dispose()
  {
    ClearCaches(disconnect: true);
  }

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
      s_shellViewModel.IsSessionConnected == true &&
      s_shellViewModel.ShowSplashScreen == false &&
      s_shellViewModel.m_blockingProgressInstances.Count == 0,
      delay: 250, // in ms
      retries: 60 // or 15 seconds
    );

  /// <summary>
  /// Creates a new user session and connects MTGO to the main server.
  /// </summary>
  /// <param name="username">The user's login name.</param>
  /// <param name="password">The user's login password.</param>
  /// <returns>The user's session id.</returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the client is already logged in.
  /// </exception>
  /// <exception cref="ArgumentException">
  /// Thrown when the user's credentials are missing or malformed.
  /// </exception>
  /// <exception cref="TimeoutException">
  /// Thrown when the client times out trying to connect and initialize.
  /// </exception>
  public async Task<Guid> LogOn(string username, SecureString password)
  {
    if (IsLoggedIn)
      throw new InvalidOperationException("Cannot log on while logged in.");

    // Initializes the login manager if it has not already been initialized.
    dynamic LoginVM = Unbind(s_loginManager);
    if (!LoginVM.IsLoginEnabled)
    {
      Log.Trace("Initializing the login manager.");
      LoginVM.Initialize();
    }

    // Passes the user's credentials to the MTGO client for authentication.
    LoginVM.ScreenName = username;
    LoginVM.Password = password.RemoteSecureString();
    if (!LoginVM.LogOnCanExecute())
      throw new ArgumentException("Missing one or more user credentials.");

    // Executes the login command and creates a new task to connect the client.
    Log.Debug("Logging in as {Username}.", username);
    LoginVM.LogOnExecute();
    if (!(await WaitForClientReady()))
      throw new TimeoutException(
          "Failed to connect and initialize the client.");

    // Explicitly update state for a non-interactive session.
    LoginVM.IsLoggedIn = true;
    LoginVM.IsLoginEnabled = false;

    return SessionId;
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
    s_session.LogOff();
    Try(() => Unbind(s_loginManager).IsLoginEnabled = true);
    if (!(await WaitUntil(() => !IsConnected || RemoteClient.IsDisposed ))) {
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
