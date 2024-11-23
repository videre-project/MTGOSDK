/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API;

/// <summary>
/// Configurable options for the client's startup and connection process.
/// </summary>
public struct ClientOptions()
{
  /// <summary>
  /// Whether to start a new MTGO process, or attach to an existing one.
  /// </summary>
  /// <remarks>
  /// This will also kill any existing MTGO process when starting a new one.
  /// </remarks>
  public bool CreateProcess { get; init; } = false;

  /// <summary>
  /// Whether to start the MTGO process minimized.
  /// </summary>
  /// <remarks>
  /// On older versions of Windows, this may cause buggy restore behavior after
  /// unminimizing the window. This is expected, but can reduce debuggability.
  /// </remarks>
  public bool StartMinimized { get; init; } = false;

  /// <summary>
  /// Whether to kill the MTGO process when the client object is disposed.
  /// </summary>
  public bool CloseOnExit { get; init; } = false;

  /// <summary>
  /// Whether to skip checking for the current online status of the MTGO server.
  /// </summary>
  /// <remarks>
  /// This can be useful when there is a separate outage or maintenance window
  /// in the Daybreak Census API, as this does not impact MTGO functionality.
  /// </remarks>
  public bool SkipOnlineCheck { get; init; } = false;

  /// <summary>
  /// Whether to accept the EULA prompt when starting the MTGO client.
  /// </summary>
  /// <remarks>
  /// This does not avoid acceptance of the EULA prompt in lieu of the user;
  /// the terms of the EULA are still legally binding when using the client.
  /// </remarks>
  public bool AcceptEULAPrompt { get; init; } = false;
}
