/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Memory.Snapshot;


namespace MTGOSDK.Core;

/// <summary>
/// Configurable options for diagnostic features throughout the SDK.
/// </summary>
public struct DiagnosticOptions()
{
  /// <summary>
  /// Whether to enable logging throughout the SDK.
  /// </summary>
  /// <remarks>
  /// By default, the SDK will use a NullLogger which will not log to any
  /// output. To enable logging, provide a logger factory through the
  /// <see cref="LoggerBase.SetFactoryInstance"/> method or by setting a
  /// logger provider through the <see cref="LoggerBase.SetProviderInstance"/>
  /// method.
  /// </remarks>
  /// <value>
  /// <c>true</c> to enable logging, <c>false</c> otherwise.
  /// </value>
  public bool EnableLogging { get; init; } = true;

  /// <summary>
  /// Whether to enable performance metrics throughout the SDK.
  /// </summary>
  /// <remarks>
  /// Enabling this option will allow the SDK to collect performance metrics
  /// throughout its execution. These metrics can be useful for identifying
  /// performance bottlenecks and other regression issues between client and
  /// SDK versions.
  /// </remarks>
  /// <value>
  /// <c>true</c> to enable performance metrics, <c>false</c> otherwise.
  /// </value>
  public bool EnablePerformanceMetrics { get; init; } = false;

  /// <summary>
  /// Whether to enable snapshot debugging throughout the SDK.
  /// </summary>
  /// <remarks>
  /// As the SDK takes continuous snapshots of the MTGO client, enabling this
  /// option will reserve the past few snapshots to inspect previous states of
  /// the client. This is managed through the
  /// <see cref="SnapshotRuntime"/> class to
  /// provide several copy-on-write snapshots of the client's state.
  /// Enabling snapshots can be useful for debugging behaviors intricately
  /// dependent on the client's state that are not easily reproducible.
  /// </remarks>
  /// <value>
  /// <c>true</c> to enable snapshot debugging, <c>false</c> otherwise.
  /// </value>
  public bool EnableSnapshotDebugging { get; init; } = false;

  /// <summary>
  /// The interval at which to take snapshots of the client.
  /// </summary>
  /// <remarks>
  /// This option is only relevant if <see cref="EnableSnapshotDebugging"/> is
  /// set to <c>true</c>. The SDK will take a snapshot of the client at the
  /// specified interval, allowing for a history of the client's state to be
  /// inspected.
  /// </remarks>
  public TimeSpan SnapshotInterval { get; init; } = TimeSpan.FromSeconds(30);
}
