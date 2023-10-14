/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core;

using FlsClient;
using FlsClient.Interface;


namespace MTGOSDK.API;

public class Client
{
  /// <summary>
  /// This class manages the client's connection and user session information.
  /// </summary>
  private static readonly IFlsClientSession s_flsClientSession =
    ObjectProvider.Get<FlsClientSession>();

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
  public static string Version =>
    new Proxy<IFlsClientSession>().AssemblyVersion;

  public Client()
  {
    // TODO: Add constructor parameters to set properties of the RemoteClient
    //       singleton instance prior to connecting to the MTGO process.

    // Verify that the current user session is valid.
    if (CurrentUser.Id == -1 && CurrentUser.IsLoggedIn)
      throw new Exception("User is logged in without a valid user id.");
  }
}
