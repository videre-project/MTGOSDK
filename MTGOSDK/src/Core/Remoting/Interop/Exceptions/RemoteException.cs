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
public class RemoteException(string msg, string remoteStackTrace) : Exception
{
  public string RemoteMessage { get; private set; } = msg;

  public string RemoteStackTrace => remoteStackTrace;

  public override string StackTrace =>
      $"{remoteStackTrace}\n" +
      $"--- End of remote exception stack trace ---\n" +
      $"{base.StackTrace}";

  public override string ToString() => RemoteMessage;
}
