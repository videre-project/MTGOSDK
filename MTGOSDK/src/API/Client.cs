/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Security;
using System.Security.Authentication;

using MTGOSDK.Core;

using FlsClient.Interface;
using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model;


namespace MTGOSDK.API;

public class Client
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
  /// View model for the client's login and authentication process.
  /// </summary>
  private static readonly ILoginViewModel s_loginManager =
    ObjectProvider.Get<ILoginViewModel>();

  /// <summary>
  /// Internal reference to the current logged in user.
  /// </summary>
  private User m_currentUser;

  /// <summary>
  /// Returns the current logged in user's public information.
  /// </summary>
  public User CurrentUser
  {
    get
    {
      // TODO: We cannot bind an interface type as structs are not yet supported.
      var UserInfo_t = Proxy<dynamic>.From(s_flsClientSession.LoggedInUser);

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
  public bool IsLoggedIn => CurrentUser.Id != -1;

  public Client()
  {
    // TODO: Add constructor parameters to set properties of the RemoteClient
    //       singleton instance prior to connecting to the MTGO process.

    // Verify that the current user session is valid.
    if (SessionId != Guid.Empty && IsConnected && !IsLoggedIn)
      throw new Exception("Current user session has an invalid user id.");
  }

  /// <summary>
  /// Creates a new user session and connects MTGO to the main server.
  /// </summary>
  public void LogOn(string userName, SecureString password)
  {
    if (s_loginManager.IsLoggedIn)
      throw new Exception("Cannot log in while already logged in.");

    // Initializes the login manager if it has not already been initialized.
    dynamic LoginVM = Proxy<dynamic>.From(s_loginManager);
    if (!LoginVM.IsLoginEnabled)
      LoginVM.Initialize();

    // Passes the user's credentials to the MTGO client for authentication.
    LoginVM.ScreenName = userName;
    LoginVM.Password = password.RemoteSecureString();
    if (!LoginVM.LogOnCanExecute())
      throw new AuthenticationException("Invalid or missing credentials.");

    // Executes the login command and creates a new task to connect the client.
    LoginVM.LogOnExecute();
  }

  /// <summary>
  /// Closes the current user session and returns to the login screen.
  /// </summary>
  public void LogOff() => s_session.LogOff();
}
