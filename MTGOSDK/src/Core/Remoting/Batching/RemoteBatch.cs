/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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
    if (value is null) throw new ArgumentNullException(nameof(value));

    var slot = new RemoteBatchSlot(this, this,
      new() { Kind = RemoteBatchReferenceKind.Input, Id = _inputs.Count });

    // Ignore transport timestamps when comparing capture descriptions so clients
    // can share a hook when they recorded the same inputs at different times.
    _inputs.Add(value.WithTimestamp(default));
    return slot;
  }

  /// <summary>
  /// Records a supported literal or raw remote handle without remote discovery.
  /// Object inputs remain retained for the lifetime of the batch.
  /// </summary>
  public RemoteBatchSlot Input(object? value)
  {
    if (value is ObjectOrRemoteAddress encoded)
      return Input(encoded);
    if (value is DLRWrapper)
      throw new ArgumentException("Pass an existing raw remote handle instead of a wrapper.", nameof(value));
    // Use the existing handle token so recording an input does not fetch
    // remote type metadata.
    var remote = value switch
    {
      RemoteObject instance => instance,
      DynamicRemoteObject instance => instance.__ro,
      _ => null
    };
    if (remote is not null)
    {
      try
      {
        return Input(ObjectOrRemoteAddress.FromRemoteObject(remote, typeof(object).FullName!));
      }
      finally
      {
        GC.KeepAlive(value);
      }
    }
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
  /// Copies the recorded description while preserving local ownership of its
  /// object inputs. Ownership is not included in the serialized request.
  /// </summary>
  public RemoteBatchRequest ToRequest() => CreateRequest().Copy();

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

  /// <summary>
  /// Retains one root result in the target process and returns a live remote object.
  /// Use ordinary Execute for detached values that do not require native access.
  /// </summary>
  public RemoteObject? ExecuteReference(RemoteBatchSlot value, RemoteHandle connection) =>
    ExecuteReferenceAsync(value, connection).GetAwaiter().GetResult();

  /// <summary>
  /// Retains one root result using the same ownership rules as a remote invocation.
  /// </summary>
  public async Task<RemoteObject?> ExecuteReferenceAsync(RemoteBatchSlot value, RemoteHandle connection)
  {
    var reference = Reference(value);
    var request = ToRequest();
    request.ReferenceOutput = reference;
    var result = await connection.Communicator.ExecuteDetachedBatchAsync(request).ConfigureAwait(false);
    var retained = result.Reference
      ?? throw new InvalidOperationException("Batch did not return the requested remote reference.");
    return retained.IsNull ? null
      : connection.GetRemoteObjectFromField(retained);
  }

  public RemoteBatchResult Execute(DiverCommunicator connection) =>
    ExecuteAsync(connection).GetAwaiter().GetResult();

  public async Task<RemoteBatchResult> ExecuteAsync(DiverCommunicator connection) =>
    await connection.ExecuteDetachedBatchAsync(ToRequest()).ConfigureAwait(false);

  public RemoteBatchResult Execute(RemoteHandle connection) => Execute(connection.Communicator);
  public Task<RemoteBatchResult> ExecuteAsync(RemoteHandle connection) => ExecuteAsync(connection.Communicator);
}
