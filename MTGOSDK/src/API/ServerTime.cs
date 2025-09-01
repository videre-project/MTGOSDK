/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.Core.Reflection.DLRWrapper;

using FlsClient.Interface;
using WotC.Common.Client;


namespace MTGOSDK.API;

/// <summary>
/// Utility class for converting MTGO server time to client time.
/// </summary>
public static class ServerTime
{
  /// <summary>
  /// Provides basic information about the current user and client session.
  /// </summary>
  private static readonly IFlsClientSession s_flsClientSession =
    ObjectProvider.Get<IFlsClientSession>();

  private static IServerTime s_serverTime => s_flsClientSession.ServerTime;

  //
  // IServerTime wrapper methods
  //

  public static TimeSpan ServerTimeAsRelativetime(DateTime serverTime) =>
    Unbind(s_serverTime).ServerTimeAsRelativetime(serverTime);

  public static DateTime ServerTimeAsClientTime(DateTime serverTime) =>
    s_serverTime.ServerTimeAsClientTime(serverTime);

  public static bool IsServerTimeInFuture(DateTime serverTime) =>
    ServerTimeAsRelativetime(serverTime) > TimeSpan.Zero;
}