/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
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
  private readonly Delegate _callback;
  private readonly bool _hasCapture;
  private string[]? _propertyNames;
  private int _isDisposed;

  public event PropertyChangedEventHandler? PropertyChanged;

  internal RemoteCaptureSequence? CaptureSequence { get; }

  public RemotePropertyChangeProxy(RemoteObject remoteObject, RemoteEventCapture? capture = null,
    string[]? propertyNames = null)
  {
    _remoteObject = remoteObject ??
      throw new ArgumentNullException(nameof(remoteObject));
    _hasCapture = capture is not null;
    _propertyNames = capture?.Scope is null ? propertyNames : Array.Empty<string>();
    if (capture is null)
    {
      _callback = (Action<dynamic, dynamic>)OnRemotePropertyChanged;
      _remoteObject.EventSubscribe(nameof(INotifyPropertyChanged.PropertyChanged), _callback);
    }
    else
    {
      Action<HookContext> callback = context =>
      {
        if (Volatile.Read(ref _isDisposed) != 0) return;
        var nameValue = context.Capture?.Values["event.propertyName"];
        string? name = nameValue?.Kind == RemoteBatchValueKind.Scalar
          ? nameValue.GetScalar<string>() : null;
        PropertyChanged?.Invoke(this,
          new RemotePropertyChangedEventArgs(_remoteObject.RemoteToken, name ?? "", this)
          { CaptureContext = context });
      };
      _callback = callback;
      CaptureSequence = _remoteObject.EventSubscribe(nameof(INotifyPropertyChanged.PropertyChanged), callback, capture, _propertyNames);
    }
  }

  /// <summary>
  /// Adds the property-name output to an independent copy of the caller's batch.
  /// The router uses this same description to identify and register the capture.
  /// </summary>
  internal static RemoteEventCapture CreateCapture(RemoteBatch batch, RemoteCaptureScope? scope)
  {
    var capture = new RemoteEventCapture { Scope = scope };
    capture.Batch = batch.ToRequest(copy =>
    {
      var arguments = copy.Input(ObjectOrRemoteAddress.Null);
      copy.Output("event.propertyName", copy.Read(arguments, "PropertyName"));
      capture.Inputs.Add(arguments.Reference.Id, 1);
    });
    return capture;
  }

  internal void SetTrackedProperties(string[] propertyNames)
  {
    if (_hasCapture && (_propertyNames is null || !_propertyNames.SequenceEqual(propertyNames)))
    {
      _remoteObject.SetEventPropertyFilter(_callback, propertyNames);
      _propertyNames = propertyNames;
    }
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
