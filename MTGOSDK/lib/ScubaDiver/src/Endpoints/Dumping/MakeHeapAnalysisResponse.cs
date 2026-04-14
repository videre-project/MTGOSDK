/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeHeapSnapshotResponse()
  {
    var request = DeserializeRequest<HeapSnapshotRequest>();
    int topN = request?.TopN ?? 50;
    var sw = Stopwatch.StartNew();
    Log.Debug("[Diver] Starting heap snapshot (topN={TopN})...", topN);
    var result = _runtime.TakeHeapSnapshot(topN);
    sw.Stop();
    Log.Debug("[Diver] Heap snapshot complete: {Types} types, {Objects} objects, {Size} bytes in {Ms}ms",
      result.Types?.Count ?? 0, result.TotalObjectCount, result.TotalHeapSize, sw.ElapsedMilliseconds);
    return WrapSuccess(result);
  }

  private byte[] MakeRetainChainResponse()
  {
    var request = DeserializeRequest<RetainChainRequest>();
    var sw = Stopwatch.StartNew();
    Log.Debug("[Diver] Computing retain chain for {Type}...", request.TypeName);
    var result = _runtime.GetRetainChain(request.TypeName, request.MaxDepth);
    sw.Stop();
    Log.Debug("[Diver] Retain chain complete: {Depth} entries in {Ms}ms",
      result.Chain?.Count ?? 0, sw.ElapsedMilliseconds);
    return WrapSuccess(result);
  }

  private byte[] MakeTypeInstancesResponse()
  {
    var request = DeserializeRequest<TypeInstancesRequest>();
    var result = _runtime.GetTypeInstances(
      request.TypeName,
      request.MaxCount);
    return WrapSuccess(result);
  }

  private byte[] MakeStaticHoldersResponse()
  {
    var request = DeserializeRequest<StaticHoldersRequest>();
    int topN = request?.TopN ?? 50;
    var sw = Stopwatch.StartNew();
    Log.Debug("[Diver] Analyzing static holders (topN={TopN})...", topN);
    var result = _runtime.AnalyzeStaticHolders(topN);
    sw.Stop();
    Log.Debug("[Diver] Static holders complete: {Count} holders ({Total} total), {Bytes} retained in {Ms}ms",
      result.Holders?.Count ?? 0, result.TotalStaticRoots, result.TotalRetainedBytes, sw.ElapsedMilliseconds);
    return WrapSuccess(result);
  }
}
