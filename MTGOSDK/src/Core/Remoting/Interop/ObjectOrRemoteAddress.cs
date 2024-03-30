/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;

using MTGOSDK.Core.Remoting.Interop.Utils;


namespace MTGOSDK.Core.Remoting.Interop
{
  /// <summary>
  /// Either an encoded object (for primitive types like int, string, primitibe arrays...) or info of a remote object
  /// </summary>
  public class ObjectOrRemoteAddress
  {
    /// <summary>
    /// Whether <see cref="RemoteAddress"/> or <see cref="EncodedObject"/> are set.
    /// </summary>
    public bool IsRemoteAddress { get; set; }
    public bool IsType { get; set; }
    public string Type { get; set; }
    public string Assembly { get; set; }
    public ulong RemoteAddress { get; set; }
    public string EncodedObject { get; set; }
    public bool IsNull => IsRemoteAddress && RemoteAddress == 0;

    public static ObjectOrRemoteAddress FromObj(object o) =>
      new() {
        EncodedObject = PrimitivesEncoder.Encode(o),
        Type = o.GetType().FullName
      };

    public static ObjectOrRemoteAddress FromToken(ulong addr, string type) =>
      new() {
        IsRemoteAddress = true,
        RemoteAddress = addr,
        Type = type
      };

    public static ObjectOrRemoteAddress Null =>
      new() {
        IsRemoteAddress = true,
        RemoteAddress = 0,
        Type = typeof(object).FullName
      };

    public static ObjectOrRemoteAddress FromType(Type type) =>
      new() {
        Type = type.FullName,
        Assembly = type.Assembly.GetName().Name,
        IsType = true
      };
  }
}
