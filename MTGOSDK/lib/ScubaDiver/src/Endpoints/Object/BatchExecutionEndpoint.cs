/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Linq;

using MessagePack;

using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace ScubaDiver;

/// <summary>
/// Decodes batch requests, executes them, and encodes values or request-level errors.
/// </summary>
public sealed class BatchExecutionEndpoint
{
  private static readonly ConcurrentDictionary<(string FullName, string AssemblyName), Type> s_types = new();
  private readonly BatchExecutor _executor;
  private readonly Func<Func<RemoteBatchResult>, RemoteBatchResult> _dispatch;

  public BatchExecutionEndpoint(BatchExecutor executor,
    Func<Func<RemoteBatchResult>, RemoteBatchResult> dispatch)
  {
    _executor = executor;
    _dispatch = dispatch;
  }

  /// <summary>
  /// Resolves explicit assembly identities against loaded target assemblies.
  /// Uses the runtime resolver only when no assembly name is supplied.
  /// </summary>
  public static Type ResolveLoadedType(RemoteBatchType identity, Func<string, Type> fallback)
  {
    if (identity.AssemblyName == null)
      return fallback(identity.FullName);
    return s_types.GetOrAdd((identity.FullName, identity.AssemblyName), key =>
    {
      var matches = AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name == key.AssemblyName || assembly.FullName == key.AssemblyName)
        .Select(assembly => assembly.GetType(key.FullName, false))
        .Where(type => type != null).ToArray();
      if (matches.Length != 1)
        throw new TypeLoadException($"Expected one loaded type {key.FullName} in {key.AssemblyName}; found {matches.Length}.");
      return matches[0]!;
    });
  }

  /// <summary>
  /// Executes a decoded request, dispatching the whole batch when ForceUIThread
  /// is set. The resulting values are then encoded on the request thread.
  /// </summary>
  public byte[] Handle(byte[] body)
  {
    try
    {
      var request = MessagePackSerializer.Deserialize<RemoteBatchRequest>(body);
      var result = request.ForceUIThread
        ? _dispatch(() => _executor.Execute(request)) : _executor.Execute(request);
      return MessagePackSerializer.Serialize(DiverResponse<RemoteBatchResult>.Success(result));
    }
    catch (Exception ex)
    {
      return MessagePackSerializer.Serialize(
        DiverResponse<RemoteBatchResult>.FromError(ex.Message, ex.ToString()));
    }
  }
}
