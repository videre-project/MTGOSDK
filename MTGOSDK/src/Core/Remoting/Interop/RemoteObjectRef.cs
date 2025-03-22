/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace MTGOSDK.Core.Remoting.Interop;

internal class RemoteObjectRef(
  ObjectDump remoteObjectInfo,
  TypeDump typeInfo,
  DiverCommunicator creatingCommunicator)
{
  private bool _isReleased = false;

  // TODO: I think addresses as token should be reworked
  public ulong Token => remoteObjectInfo.PinnedAddress;
  public DiverCommunicator Communicator => creatingCommunicator;

  public TypeDump GetTypeDump() => typeInfo;

  private void ThrowIfReleased()
  {
    if (_isReleased)
    {
      throw new ObjectDisposedException(
          "Cannot use RemoteObjectRef object after `Release` have been called");
    }
  }

  /// <summary>
  /// Gets the value of a remote field.
  /// </summary>
  /// <param name="name">Name of field to get the value of</param>
  /// <param name="refresh">Whether to refresh or use a cached value</param>
  public MemberDump GetFieldDump(string name, bool refresh = false)
  {
    ThrowIfReleased();
    if (refresh)
    {
      remoteObjectInfo = creatingCommunicator
        .DumpObject(remoteObjectInfo.PinnedAddress, remoteObjectInfo.Type);
    }

    var field = remoteObjectInfo.Fields.Single(fld => fld.Name == name);
    if (!string.IsNullOrEmpty(field.RetrievalError))
      throw new Exception(
        $"Field of the remote object could not be retrieved. Error: {field.RetrievalError}");

    // field has a value. Returning as-is for the user to parse
    return field;
  }

  /// <summary>
  /// Gets the value of a remote property.
  /// </summary>
  /// <param name="name">Name of property to get the value of</param>
  /// <param name="refresh">Whether to refresh or use a cached value</param>
  public MemberDump GetProperty(string name, bool refresh = false)
  {
    ThrowIfReleased();
    if (refresh)
    {
      throw new NotImplementedException("Refreshing property values not supported yet");
    }

    var property = remoteObjectInfo.Properties.Single(prop => prop.Name == name);
    if (!string.IsNullOrEmpty(property.RetrievalError))
    {
      throw new Exception(
        $"Property of the remote object could not be retrieved. Error: {property.RetrievalError}");
    }

    // Property has a value. Returning as-is for the user to parse.
    return property;
  }

  public InvocationResults InvokeMethod(
    string methodName,
    string[] genericArgsFullTypeNames,
    ObjectOrRemoteAddress[] args)
  {
    ThrowIfReleased();
    return creatingCommunicator
      .InvokeMethod(
          remoteObjectInfo.PinnedAddress,
          remoteObjectInfo.Type,
          methodName,
          genericArgsFullTypeNames,
          args);
  }

  public InvocationResults SetField(
    string fieldName,
    ObjectOrRemoteAddress newValue)
  {
    ThrowIfReleased();
    return creatingCommunicator
      .SetField(
          remoteObjectInfo.PinnedAddress,
          remoteObjectInfo.Type,
          fieldName,
          newValue);
  }
  public InvocationResults GetField(string fieldName)
  {
    ThrowIfReleased();
    return creatingCommunicator
      .GetField(
          remoteObjectInfo.PinnedAddress,
          remoteObjectInfo.Type,
          fieldName);
  }

  public void EventSubscribe(
    string eventName,
    DiverCommunicator.LocalEventCallback callbackProxy)
  {
    ThrowIfReleased();
    creatingCommunicator.EventSubscribe(remoteObjectInfo.PinnedAddress, eventName, callbackProxy);
  }

  public void EventUnsubscribe(
    string eventName,
    DiverCommunicator.LocalEventCallback callbackProxy)
  {
    ThrowIfReleased();
    creatingCommunicator.EventUnsubscribe(callbackProxy);
  }

  public override string ToString() =>
    string.Format(
      "RemoteObjectRef. Address: {0}, TypeFullName: {1}",
      remoteObjectInfo.PinnedAddress,
      typeInfo.Type);

  internal ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
  {
    return creatingCommunicator.GetItem(Token, key);
  }

  private static readonly Random _random = new Random();
  private static readonly TimeSpan _minDelay = TimeSpan.FromSeconds(1);
  private static readonly TimeSpan _maxDelay = TimeSpan.FromSeconds(30);

  /// <summary>
  /// Releases hold of the remote object in the remote process and the local proxy.
  /// </summary>
  public void RemoteRelease()
  {
    // Check if already released to avoid redundant calls
    if (_isReleased) return;

    // Guard against disconnected communicator
    if (creatingCommunicator is null || !creatingCommunicator.IsConnected)
    {
      _isReleased = true;
      return;
    }

    try
    {
      // Calculate exponential backoff with jitter
      var baseDelay = Math.Min(
        _maxDelay.TotalMilliseconds,
        _minDelay.TotalMilliseconds * (1 + _random.NextDouble())
      );

      // Add jitter between 80-120% of base delay
      var jitteredDelay = (int)(baseDelay * (0.8 + (_random.NextDouble() * 0.4)));

      // Use Task.Delay instead of Thread.Sleep to avoid blocking
      Task.Delay(jitteredDelay).Wait();

      creatingCommunicator.UnpinObject(remoteObjectInfo.PinnedAddress);
    }
    catch (NullReferenceException)
    {
      // Ignore null reference exceptions
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to release remote object");
    }
    finally
    {
      _isReleased = true;
    }
  }
}
