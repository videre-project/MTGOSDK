/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// An exact method or constructor signature. Constructor name is .ctor.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchMethod
{
  [Key(0)]
  public RemoteBatchType DeclaringType { get; set; } = new();

  [Key(1)]
  public string Name { get; set; } = "";

  [Key(2)]
  public RemoteBatchType[] ParameterTypes { get; set; } = [];

  [Key(3)]
  public RemoteBatchType[] GenericArguments { get; set; } = [];

  public RemoteBatchMethod() { }
  public RemoteBatchMethod(RemoteBatchType declaringType, string name,
    params RemoteBatchType[] parameterTypes)
  {
    DeclaringType = declaringType;
    Name = name;
    ParameterTypes = parameterTypes;
  }
}
