/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Security;
using System.Security.Authentication;

using MTGOSDK.API.Users;
using MTGOSDK.API.Interface;
using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Security;

using FlsClient.Interface;
using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model;


namespace MTGOSDK.API;
using static MTGOSDK.API.Events;

public sealed class Client : DLRWrapper<dynamic>, IDisposable
{
  /// <summary>
  /// Manages the client's connection and user session information.
  /// </summary>
  internal static readonly ISession s_session =
    Defer(ObjectProvider.Get<ISession>);

  /// <summary>
  /// Provides basic information about the current user and client session.
  /// </summary>
  private static readonly IFlsClientSession s_flsClientSession =
    Defer(ObjectProvider.Get<IFlsClientSession>);

  /// <summary>
  /// View model for the client's login and authentication process.
  /// </summary>
  private static readonly ILoginViewModel s_loginManager =
    Defer(ObjectProvider.Get<ILoginViewModel>);

  /// <summary>
  /// Internal reference to the current logged in user.
  /// </summary>
  private User? m_currentUser;

  /// <summary>
  /// Returns the current logged in user's public information.
  /// </summary>
  public User CurrentUser
  {
    get
    {
      // TODO: We cannot bind an interface type as structs are not yet supported.
      var UserInfo_t = Unbind(s_flsClientSession.LoggedInUser);

      // Only fetch and update the current user if the user Id has changed.
      if (UserInfo_t.Id != m_currentUser?.Id)
        m_currentUser = new User(UserInfo_t.Id, UserInfo_t.Name);

      return m_currentUser;
    }
  }

  /// <summary>
  /// The latest version of the MTGO client that this SDK is compatible with.
  /// </summary>
  public static string Version => new Proxy<IClientSession>().AssemblyVersion;

  /// <summary>
  /// The MTGO client's user session id.
  /// </summary>
  public Guid SessionId => new(s_flsClientSession.SessionId);

  /// <summary>
  /// Whether the client is currently online and connected.
  /// </summary>
  public bool IsConnected => s_flsClientSession.IsConnected;

  /// <summary>
  /// Whether the client is currently logged in.
  /// </summary>
  public bool IsLoggedIn => s_loginManager.IsLoggedIn;

  /// <summary>
  /// Creates a new instance of the MTGO client API.
  /// </summary>
  /// <param name="options">The client's configuration options.</param>
  /// <remarks>
  /// This class is used to manage the client's connection and user session,
  /// and should be instantiated once per application instance and prior to
  /// invoking with other API classes.
  /// </remarks>
  /// <exception cref="VerificationException">
  /// Thrown when the current user session is invalid.
  /// </exception>
  public Client(ClientOptions options = default) : base(
    factory: async delegate
    {
      // Starts a new MTGO client process.
      if (options.CreateProcess && !(await RemoteClient.StartProcess()))
        throw new Exception("Failed to start the MTGO client process.");

      // Sets the client's disposal policy.
      if(options.DestroyOnExit)
        RemoteClient.DestroyOnExit = true;

      // Configure the remote client connection.
      if(options.Port != null)
        RemoteClient.Port = Cast<ushort>(options.Port);
    })
  {
    // Ensures all deferred static fields in the queue are initialized.
    Construct(_ref: s_flsClientSession /* Can be any deferred instance */);

    // Closes any blocking dialogs preventing the client from logging in.
    if (options.AcceptEULAPrompt)
      WindowUtilities.CloseDialogs();

    // Verify that any existing user sessions are valid.
    if (SessionId != Guid.Empty && IsConnected && CurrentUser.Id == -1)
      throw new VerificationException("Current user session is invalid.");
  }

  /// <summary>
  /// Waits until the client has connected and is ready to be interacted with.
  /// </summary>
  /// <returns>Whether the client is ready.</returns>
  /// <remarks>
  /// The client may take a few seconds to close the overlay when done loading.
  /// </remarks>
  public async Task<bool> WaitForClientReady()
  {
    var shellViewModel = ObjectProvider.Get<IShellViewModel>(bindTypes: false);
    return await WaitUntil(() =>
      shellViewModel.IsSessionConnected == true &&
      shellViewModel.ShowSplashScreen == false &&
      shellViewModel.m_blockingProgressInstances.Count == 0,
      delay: 250, // in ms
      retries: 60 // or 15 seconds
    );
  }

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
  public async Task<Guid> LogOn(string username, SecureString password)
  {
    if (IsLoggedIn)
      throw new InvalidOperationException("Cannot log on while logged in.");

    // Initializes the login manager if it has not already been initialized.
    dynamic LoginVM = Unbind(s_loginManager);
    if (!LoginVM.IsLoginEnabled)
      LoginVM.Initialize();

    // Passes the user's credentials to the MTGO client for authentication.
    LoginVM.ScreenName = username;
    LoginVM.Password = password.RemoteSecureString();
    if (!LoginVM.LogOnCanExecute())
      throw new ArgumentException("Missing one or more user credentials.");

    // Executes the login command and creates a new task to connect the client.
    LoginVM.LogOnExecute();
    if (!(await WaitForClientReady()))
      throw new Exception("Failed to connect and initialize the client.");

    return this.SessionId;
  }

  /// <summary>
  /// Closes the current user session and returns to the login screen.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the client is not currently logged in.
  /// </exception>
  public async Task LogOff()
  {
    if (!IsLoggedIn)
      throw new InvalidOperationException("Cannot log off while logged out.");

    // Invokes logoff command and disconnects the MTGO client.
    s_session.LogOff();
    if (!(await WaitUntil(() => !IsConnected)))
      throw new Exception("Failed to log off and disconnect the client.");
  }

  /// <summary>
  /// Disposes of the remote client handle.
  /// </summary>
  public void Dispose() => RemoteClient.Dispose();

  //
  // ISession wrapper events
  //

  public EventProxy<SystemAlertEventArgs> SystemAlertReceived =
    new(/* ISession */ s_session, nameof(SystemAlertReceived));

  public EventProxy<ErrorEventArgs> LogOnFailed =
    new(/* ISession */ s_session, nameof(LogOnFailed));

  public EventProxy<ErrorEventArgs> ErrorReceived =
    new(/* ISession */ s_session, nameof(ErrorReceived));

  public EventProxy IsConnectedChanged =
    new(/* ISession */ s_session, nameof(IsConnectedChanged));
}
