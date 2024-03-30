/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Linq;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace MTGOSDK.Core.Remoting.Internal;

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

  /// <summary>
  /// Gets the value of a remote field. Returned value might be a cached version unless <see cref="refresh"/> is set to True.
  /// </summary>
  /// <param name="name">Name of field to get the value of</param>
  /// <param name="refresh">Whether the value should be read again for this invocation or a cache version is good enough</param>
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
  /// Gets the value of a remote property. Returned value might be a cached version unless <see cref="refresh"/> is set to True.
  /// </summary>
  /// <param name="name">Name of property to get the value of</param>
  /// <param name="refresh">Whether the value should be read again for this invocation or a cache version is good enough</param>
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

    // property has a value. Returning as-is for the user to parse
    return property;
  }

  private void ThrowIfReleased()
  {
    if (_isReleased)
    {
      throw new ObjectDisposedException("Cannot use RemoteObjectRef object after `Release` have been called");
    }
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

  /// <summary>
  /// Releases hold of the remote object in the remote process and the local proxy.
  /// </summary>
  public void RemoteRelease()
  {
    creatingCommunicator.UnpinObject(remoteObjectInfo.PinnedAddress);
    _isReleased = true;
  }

  public override string ToString()
  {
    return $"RemoteObjectRef. Address: {remoteObjectInfo.PinnedAddress}, TypeFullName: {typeInfo.Type}";
  }

  internal ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
  {
    return creatingCommunicator.GetItem(Token, key);
  }
}
