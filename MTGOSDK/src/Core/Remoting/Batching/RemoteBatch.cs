/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// Records reflection operations for execution in one remote request.
/// </summary>
/// <remarks>
/// Execution takes a detached copy of the recorded description so request settings
/// can be applied independently of the builder. Creating that copy requires a stable
/// description, so callers must serialize editing and execution.
/// </remarks>
public sealed class RemoteBatch : RemoteBatchScope
{
  internal int NextId;
  private readonly List<ObjectOrRemoteAddress> _inputs = [];
  private readonly Dictionary<string, RemoteBatchProjection> _projections = new();

  /// <summary>
  /// Sets the maximum output depth, measured from depth zero at the output root.
  /// A deeper value causes serialization to fail.
  /// </summary>
  public int MaxDepth { get; set; } = 32;

  /// <summary>
  /// Requests dispatcher execution for the entire batch.
  /// </summary>
  public bool ForceUIThread { get; set; }

  public RemoteBatch()
  {
    Owner = this;
  }

  /// <summary>
  /// Records an encoded literal or an existing remote handle.
  /// </summary>
  public RemoteBatchSlot Input(ObjectOrRemoteAddress value)
  {
    var slot = new RemoteBatchSlot(this, this,
      new() { Kind = RemoteBatchReferenceKind.Input, Id = _inputs.Count });
    _inputs.Add(value ?? throw new ArgumentNullException(nameof(value)));
    return slot;
  }

  /// <summary>
  /// Records a supported literal or raw remote handle without remote discovery.
  /// </summary>
  public RemoteBatchSlot Input(object? value)
  {
    if (value is ObjectOrRemoteAddress encoded)
      return Input(encoded);
    if (value is DLRWrapper)
      throw new ArgumentException("Pass an existing raw remote handle instead of a wrapper.", nameof(value));
    // Use the existing handle token so recording an input does not fetch
    // remote type metadata.
    if (value is RemoteObject remote)
      return Input(ObjectOrRemoteAddress.FromToken(remote.RemoteToken, typeof(object).FullName!));
    if (value is DynamicRemoteObject dynamicRemote)
      return Input(ObjectOrRemoteAddress.FromToken(dynamicRemote.__ro.RemoteToken, typeof(object).FullName!));
    if (value != null && TypeProxy<dynamic>.IsProxy(value))
      throw new ArgumentException("Pass an existing raw remote handle instead of a proxy.", nameof(value));
    return Input(RemoteFunctionsInvokeHelper.CreateRemoteParameter(value!));
  }

  /// <summary>
  /// Registers a named projection, including recursive member projections.
  /// </summary>
  public void DefineProjection(string name, RemoteBatchProjection projection) =>
    _projections.Add(name, projection);

  /// <summary>
  /// Copies the recorded batch into an independent wire request.
  /// </summary>
  public RemoteBatchRequest ToRequest() => MessagePackSerializer.Deserialize<RemoteBatchRequest>(
    MessagePackSerializer.Serialize(CreateRequest()));

  private RemoteBatchRequest CreateRequest() => new()
  {
    Inputs = _inputs,
    Operations = Operations,
    Projections = _projections,
    MaxDepth = MaxDepth,
    ForceUIThread = ForceUIThread
  };

  /// <summary>
  /// Composes additional operations on a detached copy and returns its request.
  /// </summary>
  internal RemoteBatchRequest ToRequest(Action<RemoteBatch> configure)
  {
    var request = ToRequest();
    var copy = new RemoteBatch
    {
      MaxDepth = request.MaxDepth,
      ForceUIThread = request.ForceUIThread,
      NextId = NextId
    };
    copy._inputs.AddRange(request.Inputs);
    copy.Operations.AddRange(request.Operations);
    foreach (var projection in request.Projections)
      copy._projections.Add(projection.Key, projection.Value);
    configure(copy);
    return copy.CreateRequest();
  }

  public RemoteBatchResult Execute(DiverCommunicator connection) =>
    ExecuteAsync(connection).GetAwaiter().GetResult();

  public Task<RemoteBatchResult> ExecuteAsync(DiverCommunicator connection) =>
    connection.ExecuteDetachedBatchAsync(ToRequest());
  public RemoteBatchResult Execute(RemoteHandle connection) => Execute(connection.Communicator);
  public Task<RemoteBatchResult> ExecuteAsync(RemoteHandle connection) => ExecuteAsync(connection.Communicator);
}
