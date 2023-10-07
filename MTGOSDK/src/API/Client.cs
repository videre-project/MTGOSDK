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
  /// The <c>FlsClient.FlsClientSession</c> object.
  /// <para>
  /// This class manages the client's connection and user session information.
  /// </para>
  /// </summary>
  private static readonly dynamic s_flsClientSession =
    ObjectProvider.Get<FlsClientSession>();

  public User CurrentUser { get; private set; }

  public Client()
  {
    // TODO: Add constructor parameters to set properties of the RemoteClient
    //       singleton instance prior to connecting to the MTGO process.

    // We cannot bind the interface type as structs are not yet supported.
    var userInfo = Proxy<dynamic>.From(s_flsClientSession.LoggedInUser).Info;
    CurrentUser = new User(userInfo);
  }
}
