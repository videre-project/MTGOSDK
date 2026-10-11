/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;

using MTGOSDK.Core.Remoting.Hooking;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Event data for a token-tracked property change notification.
/// </summary>
public sealed class RemotePropertyChangedEventArgs(
  ulong token,
  string propertyName,
  INotifyPropertyChanged source) : PropertyChangedEventArgs(propertyName)
{
  /// <summary>
  /// Gets the <see cref="MTGOSDK.Core.Memory.Snapshot.SnapshotRuntime"/> token associated with the source object.
  /// </summary>
  public ulong Token { get; } = token;

  /// <summary>
  /// Gets the source object that raised <see cref="INotifyPropertyChanged.PropertyChanged"/>.
  /// </summary>
  public INotifyPropertyChanged Source { get; } = source;

  /// <summary>
  /// Values detached in the native notification or at an enclosing scope's
  /// completion, when capture was requested.
  /// </summary>
  public HookContext? CaptureContext { get; init; }
}
