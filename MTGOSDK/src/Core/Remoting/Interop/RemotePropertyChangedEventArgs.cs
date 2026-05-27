/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Event data for a token-tracked property change notification.
/// </summary>
public sealed class RemotePropertyChangedEventArgs(
  ulong token,
  string propertyName,
  INotifyPropertyChanged source) : EventArgs
{
  /// <summary>
  /// Gets the <see cref="MTGOSDK.Core.Memory.Snapshot.SnapshotRuntime"/> token associated with the source object.
  /// </summary>
  public ulong Token { get; } = token;

  /// <summary>
  /// Gets the name of the property that changed.
  /// </summary>
  public string PropertyName { get; } = propertyName;

  /// <summary>
  /// Gets the source object that raised <see cref="INotifyPropertyChanged.PropertyChanged"/>.
  /// </summary>
  public INotifyPropertyChanged Source { get; } = source;
}
