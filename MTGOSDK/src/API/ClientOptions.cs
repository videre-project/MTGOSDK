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
  public bool CreateProcess { get; init; } = false;

  /// <summary>
  /// Whether to start the MTGO process minimized.
  /// </summary>
  public bool StartMinimized { get; init; } = false;

  /// <summary>
  /// Whether to kill the MTGO process when the client object is disposed.
  /// </summary>
  public bool DestroyOnExit { get; init; } = false;

  /// <summary>
  /// Whether to accept the EULA prompt when starting the MTGO client.
  /// </summary>
  /// <remarks>
  /// This does not avoid acceptance of the EULA prompt in lieu of the user;
  /// the terms of the EULA are still legally binding when using the client.
  /// </remarks>
  public bool AcceptEULAPrompt { get; init; } = false;
}
