/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Bridges a remote object's PropertyChanged event into a local
/// <see cref="INotifyPropertyChanged"/> source.
/// </summary>
internal sealed class RemotePropertyChangeProxy :
  INotifyPropertyChanged,
  IDisposable
{
  private readonly RemoteObject _remoteObject;
  private readonly Action<dynamic, dynamic> _callback;
  private int _isDisposed;

  public event PropertyChangedEventHandler? PropertyChanged;

  public RemotePropertyChangeProxy(RemoteObject remoteObject)
  {
    _remoteObject = remoteObject ??
      throw new ArgumentNullException(nameof(remoteObject));
    _callback = OnRemotePropertyChanged;
    _remoteObject.EventSubscribe(nameof(INotifyPropertyChanged.PropertyChanged), _callback);
  }

  private void OnRemotePropertyChanged(dynamic sender, dynamic args)
  {
    if (Volatile.Read(ref _isDisposed) != 0)
      return;

    string? propertyName = null;
    try { propertyName = (string?) args?.PropertyName; }
    catch { }

    if (string.IsNullOrEmpty(propertyName))
      return;

    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
      return;

    try
    {
      _remoteObject.EventUnsubscribe(nameof(INotifyPropertyChanged.PropertyChanged), _callback);
    }
    catch (Exception ex)
    {
      Log.Debug(ex, "Failed to unsubscribe remote PropertyChanged callback for token={Token}", _remoteObject.RemoteToken);
    }

    PropertyChanged = null;
  }
}
