/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MessagePack;

using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace MTGOSDK.UnitTests;

/// <summary>
/// Messages from an older client or Diver (without the fields added for pin
/// ownership) must read as the older behaviour.
/// </summary>
public class ProtocolCompatibility
{
  [MessagePackObject]
  public class OldBatchCollectionRequest
  {
    [Key(0)] public ulong CollectionAddress { get; set; }
    [Key(1)] public string CollectionTypeName { get; set; } = "";
    [Key(2)] public string PathsDelimited { get; set; } = "";
    [Key(3)] public int MaxItems { get; set; }
  }

  [MessagePackObject]
  public class OldRegisterClientRequest
  {
    [Key(0)] public int ProcessId { get; set; }
  }

  [MessagePackObject]
  public class OldCallbackInvocationRequest
  {
    [Key(0)] public DateTime Timestamp { get; set; }
    [Key(1)] public int Token { get; set; }
  }

  private static T Read<T>(object old) =>
    MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(old.GetType(), old));

  [Test]
  public void Test_OlderBatchRequest_StillPinsItems()
  {
    var request = Read<BatchCollectionRequest>(new OldBatchCollectionRequest
    {
      CollectionAddress = 7, CollectionTypeName = "T", PathsDelimited = "Name", MaxItems = 3
    });
    Assert.That(request.SkipItemPins, Is.False, "an older client receives its item tokens");
    Assert.That(request.MaxItems, Is.EqualTo(3));
  }

  [Test]
  public void Test_OlderRegistration_HasNoClientId()
  {
    var request = Read<RegisterClientRequest>(new OldRegisterClientRequest { ProcessId = 42 });
    Assert.That(request.ProcessId, Is.EqualTo(42));
    Assert.That(request.ClientId, Is.Null, "one registration per process, as before");
  }

  [Test]
  public void Test_OlderCallback_DoesNotOwnItsParameters()
  {
    var request = Read<CallbackInvocationRequest>(new OldCallbackInvocationRequest { Token = 5 });
    Assert.That(request.Token, Is.EqualTo(5));
    Assert.That(request.ParametersPinnedPerRecipient, Is.False,
      "an argument that is not wrapped returns no reference to a Diver that pinned once for all recipients");
  }
}
