/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Logging;

/// <summary>
/// Ambient context for correlating log entries across IPC boundaries.
/// Set by TcpServer (Diver-side) and TcpCommunicator (SDK-side) to tag
/// all logs emitted during a request with the TCP frame's MessageId.
/// </summary>
public static class LogContext
{
  /// <summary>
  /// The MessageId of the current IPC request, if any.
  /// Flows across async boundaries via AsyncLocal.
  /// </summary>
  public static readonly AsyncLocal<int?> MessageId = new();
}
