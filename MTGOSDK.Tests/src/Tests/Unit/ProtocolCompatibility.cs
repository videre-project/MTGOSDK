/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MessagePack;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
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
    [Key(2)] public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();
    [Key(3)] public RemoteBatchResult? Capture { get; set; }
    [Key(4)] public RemoteBatchError? CaptureError { get; set; }
    [Key(5)] public RemoteCaptureSequence? CaptureSequence { get; set; }
  }

  [MessagePackObject]
  public class OldResponse<T>
  {
    [Key(0)] public bool IsError { get; set; }
    [Key(1)] public string? ErrorMessage { get; set; }
    [Key(2)] public string? ErrorStackTrace { get; set; }
    [Key(3)] public T Data { get; set; } = default!;
  }

  [MessagePackObject]
  public class OldUnpinRequest
  {
    [Key(0)] public ulong Address { get; set; }
  }

  private static T Read<T>(object old) =>
    MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(old.GetType(), old));

  [Test]
  public void Test_OlderBatchRequest_DefaultsToPinningItems()
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
  public void Test_OlderCallback_DoesNotGrantPerRecipientReferences()
  {
    var original = new OldCallbackInvocationRequest
    {
      Token = 5,
      Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
      Parameters = [ObjectOrRemoteAddress.FromObj("legacy"), ObjectOrRemoteAddress.FromToken(7, "System.Object")]
    };
    var request = Read<CallbackInvocationRequest>(original);
    Assert.Multiple(() =>
    {
      Assert.That(request.Token, Is.EqualTo(original.Token));
      Assert.That(request.Timestamp, Is.EqualTo(original.Timestamp));
      Assert.That(request.Parameters, Has.Count.EqualTo(2));
      Assert.That(PrimitivesEncoder.Decode(request.Parameters[0]), Is.EqualTo("legacy"));
      Assert.That(request.Parameters[1].RemoteAddress, Is.EqualTo(7));
      Assert.That(request.PinOwner, Is.Null);
      Assert.That(request.ParametersPinnedPerRecipient, Is.False,
        "an older callback does not grant a separate reference to each recipient");
    });
  }

  [Test]
  public void Test_OlderResponse_HasNoPinOwner()
  {
    var response = Read<DiverResponse<int>>(new OldResponse<int> { Data = 7 });
    Assert.That(response.Data, Is.EqualTo(7));
    Assert.That(response.PinOwner, Is.Null);
  }

  [Test]
  public void Test_OlderClient_CanReadAResponseWithPinOwnership()
  {
    var response = Read<OldResponse<int>>(new DiverResponse<int> { Data = 7, PinOwner = "client" });
    Assert.That(response.Data, Is.EqualTo(7));
    Assert.That(response.IsError, Is.False);
  }

  [Test]
  public void Test_OlderRelease_HasNoExplicitPinOwner()
  {
    var request = Read<UnpinRequest>(new OldUnpinRequest { Address = 7 });
    Assert.That(request.Address, Is.EqualTo(7));
    Assert.That(request.PinOwner, Is.Null);
  }

}
