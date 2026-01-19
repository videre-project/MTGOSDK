/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting;

public class RemoteActivator(DiverCommunicator communicator, RemoteHandle app)
{
  public RemoteObject CreateInstance(Type t) =>
    CreateInstance(t, Array.Empty<object>());

  public RemoteObject CreateInstance(Type t,
                                      params object[] parameters) =>
    CreateInstance(t.Assembly.FullName, t.FullName, parameters);

  public RemoteObject CreateInstance(string typeFullName,
                                      params object[] parameters) =>
    CreateInstance(null, typeFullName, parameters);

  public RemoteObject CreateInstance(string assembly,
                                      string typeFullName,
                                      params object[] parameters)
  {
    object[] paramsNoEnums = parameters.ToArray();
    for (int i = 0; i < paramsNoEnums.Length; i++)
    {
      var val = paramsNoEnums[i];
      if (val.GetType().IsEnum)
      {
        var enumClass = app.GetRemoteEnum(val.GetType().FullName);
        // TODO: This breaks on the first enum value which has 2 or more flags.
        object enumVal = enumClass.GetValue(val.ToString());
        // NOTE: Object stays in place in the remote app as long as we have it's
        // reference in the paramsNoEnums array (so until end of this method)
        paramsNoEnums[i] = enumVal;
      }
    }

    ObjectOrRemoteAddress[] remoteParams = paramsNoEnums
      .Select(RemoteFunctionsInvokeHelper.CreateRemoteParameter)
      .ToArray();

    // Create object + pin
    InvocationResults invoRes = communicator
      .CreateObject(typeFullName, remoteParams);

    // Get proxy object
    var remoteObject = app.GetRemoteObjectFromField(
        invoRes.ReturnedObjectOrAddress.RemoteAddress,
        invoRes.ReturnedObjectOrAddress.Type);

    return remoteObject;
  }

  public RemoteObject CreateInstance<T>() => CreateInstance(typeof(T));

  public RemoteObject CreateArray(Type elementType, int length) =>
    CreateArray(elementType.FullName!, length);

  public RemoteObject CreateArray(string elementTypeFullName, int length)
  {
    // Create array + pin
    InvocationResults invoRes = communicator.CreateArray(elementTypeFullName, length);

    // Get proxy object
    var remoteObject = app.GetRemoteObjectFromField(
        invoRes.ReturnedObjectOrAddress.RemoteAddress,
        invoRes.ReturnedObjectOrAddress.Type);

    return remoteObject;
  }

  public RemoteObject CreateArray<T>(int length) => CreateArray(typeof(T), length);

  /// <summary>
  /// Creates an array and populates each element with a constructed object.
  /// </summary>
  /// <param name="elementTypeFullName">The full type name of the array element type.</param>
  /// <param name="constructorArgsPerElement">Constructor arguments for each element.</param>
  /// <returns>A RemoteObject wrapping the created array.</returns>
  public RemoteObject CreateArray(
    string elementTypeFullName,
    object[][] constructorArgsPerElement)
  {
    // Convert each element's constructor args to remote parameters
    var remoteCtorArgs = new List<List<ObjectOrRemoteAddress>>();
    foreach (var args in constructorArgsPerElement)
    {
      var remoteArgs = args
        .Select(RemoteFunctionsInvokeHelper.CreateRemoteParameter)
        .ToList();
      remoteCtorArgs.Add(remoteArgs);
    }

    // Create array + pin
    InvocationResults invoRes = communicator.CreateArray(
      elementTypeFullName,
      remoteCtorArgs);

    // Get proxy object
    var remoteObject = app.GetRemoteObjectFromField(
        invoRes.ReturnedObjectOrAddress.RemoteAddress,
        invoRes.ReturnedObjectOrAddress.Type);

    return remoteObject;
  }

  /// <summary>
  /// Creates an array and populates each element with a constructed object.
  /// </summary>
  /// <typeparam name="T">The element type of the array.</typeparam>
  /// <param name="constructorArgsPerElement">Constructor arguments for each element.</param>
  /// <returns>A RemoteObject wrapping the created array.</returns>
  public RemoteObject CreateArray<T>(object[][] constructorArgsPerElement) =>
    CreateArray(typeof(T).FullName!, constructorArgsPerElement);
}
