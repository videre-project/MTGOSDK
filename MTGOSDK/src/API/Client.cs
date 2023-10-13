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

  public User CurrentUser { get; private set; }

  public Client()
  {
    // TODO: Add constructor parameters to set properties of the RemoteClient
    //       singleton instance prior to connecting to the MTGO process.

    // TODO: We cannot bind an interface type as structs are not yet supported.
    var UserInfo_t = Proxy<dynamic>.From(s_flsClientSession.LoggedInUser);
    CurrentUser = User.GetUser(UserInfo_t.Id, UserInfo_t.Name);
  }
}
