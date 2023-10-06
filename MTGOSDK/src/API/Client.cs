/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core;

using FlsClient;


namespace MTGOSDK.API;

public class Client
{
  /// <summary>
  /// The <c>FlsClient.FlsClientSession</c> object.
  /// <para>
  /// This class manages the client's connection and user session information.
  /// </para>
  /// </summary>
  private static dynamic s_flsClientSession = ObjectProvider.Get<FlsClientSession>();

  public User CurrentUser { get; private set; }

  public Client()
  {
    // TODO: Add constructor parameters to set properties of the RemoteClient
    //       singleton instance prior to connecting to the MTGO process.

    CurrentUser = new User(s_flsClientSession.LoggedInUser.Info);
  }
}
