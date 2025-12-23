/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Represents either an encoded object (for primitive types like int, string,
/// primitive arrays...) or info of a remote object.
/// </summary>
[MessagePackObject]
public class ObjectOrRemoteAddress
{
  /// <summary>
  /// Whether <see cref="RemoteAddress"/> or <see cref="EncodedObject"/> are set.
  /// </summary>
  [Key(0)]
  public bool IsRemoteAddress { get; set; }
  [Key(1)]
  public bool IsType { get; set; }
  [Key(2)]
  public string Type { get; set; }
  [Key(3)]
  public string Assembly { get; set; }
  [Key(4)]
  public ulong RemoteAddress { get; set; }
  [Key(5)]
  public string EncodedObject { get; set; }
  [IgnoreMember]
  public bool IsNull => IsRemoteAddress && RemoteAddress == 0;

  [Key(6)]
  public DateTime Timestamp = DateTime.Now;

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
