/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;


namespace MTGOSDK.Core.Remoting.Interop.Exceptions;

/// <summary>
/// Encapsulates an exception that was thrown in the remote object and catched by the Diver.
/// </summary>
public class RemoteException : Exception
{
  public string RemoteMessage { get; private set; }
  private string _remoteStackTrace;
  public string RemoteStackTrace => _remoteStackTrace;
  public override string StackTrace =>
      $"{_remoteStackTrace}\n" +
      $"--- End of remote exception stack trace ---\n" +
      $"{base.StackTrace}";

  public RemoteException(string msg, string remoteStackTrace)
  {
    RemoteMessage = msg;
    _remoteStackTrace = remoteStackTrace;
  }

  public override string ToString()
  {
    return RemoteMessage;
  }
}
