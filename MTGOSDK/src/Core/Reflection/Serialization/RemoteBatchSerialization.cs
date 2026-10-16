/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Reflection.Serialization;

internal static class RemoteBatchSerialization
{
  internal static Dictionary<string, object?> ReadMembers(DynamicRemoteObject source, IEnumerable<string> paths) =>
    ReadMembersAsync(source, paths).GetAwaiter().GetResult();

  internal static async Task<Dictionary<string, object?>> ReadMembersAsync(
    DynamicRemoteObject source, IEnumerable<string> paths)
  {
    var batch = new RemoteBatch();
    batch.Output("members", batch.Input(source), CreateProjection(
      paths.Select(path => (Name: path, Path: path))));
    var result = await batch.ExecuteAsync(source.__ra).ConfigureAwait(false);
    GC.KeepAlive(source);
    return ReadMembers(result.Values["members"]);
  }

  internal static RemoteBatchProjection CreateProjection(
    IEnumerable<(string Name, string Path)> paths)
  {
    var projection = RemoteBatchProjection.Object(paths.Select(path =>
      (path.Name, path.Path, RemoteBatchProjection.Value())).ToArray());
    foreach (var member in projection.Members.Values)
      member.IgnoreErrors = true;
    return projection;
  }

  internal static void RecordCollection<TInterface, TSource>(
    RemoteBatch batch, RemoteBatchSlot source, string? prefix, int maxItems)
  {
    var paths = AccessPathAnalyzer.GetBatchablePathsForInterface<TSource, TInterface>();
    var reverseMap = AccessPathAnalyzer.GetReversePathMap(typeof(TSource), typeof(TInterface));
    var members = paths.Select(path => (
      Name: reverseMap.TryGetValue(path, out var name) ? name : path,
      Path: string.IsNullOrEmpty(prefix) ? path : prefix + "." + path)).ToList();
    var covered = members.Select(member => member.Name.Split('.')[0]).ToHashSet();
    members.AddRange(typeof(TInterface).GetProperties()
      .Where(property => !covered.Contains(property.Name))
      .Select(property => (property.Name, property.Name)));

    RecordCollection(batch, source, members, maxItems);
  }

  internal static void RecordCollection(RemoteBatch batch, RemoteBatchSlot source,
    IEnumerable<(string Name, string Path)> members, int maxItems)
  {
    if (maxItems > 0)
      source = batch.Take(source, maxItems);
    var projection = CreateProjection(members);
    batch.ForEach(source, row => row.Output("item", row.Item, projection));
  }

  internal static IEnumerable<TInterface> ReadCollection<TInterface>(RemoteBatchResult result)
    where TInterface : class => result.Rows.Select(row =>
    {
      if (row.Error != null)
        throw new InvalidOperationException(
          $"Collection item {row.SourceIndex} could not be captured: {row.Error.Message}");
      return InterfaceProxyBuilder.Create<TInterface>(ReadMembers(row.Values["item"]));
    });

  private static Dictionary<string, object?> ReadMembers(RemoteBatchValue value) =>
    value.Members.ToDictionary(member => member.Key,
      member => RemoteBatchObject.FromValue(member.Value));
}
