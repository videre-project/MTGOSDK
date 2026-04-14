/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

//
// Requests
//

[MessagePackObject]
public class HeapSnapshotRequest
{
  [Key(0)]
  public int TopN { get; set; } = 50;
}

[MessagePackObject]
public class RetainPathRequest
{
  [Key(0)]
  public ulong TargetAddress { get; set; }

  [Key(1)]
  public int MaxDepth { get; set; } = 10;

  [Key(2)]
  public int MaxPaths { get; set; } = 3;
}

[MessagePackObject]
public class TypeInstancesRequest
{
  [Key(0)]
  public string TypeName { get; set; }

  [Key(1)]
  public int MaxCount { get; set; } = 20;
}

//
// Responses
//

[MessagePackObject]
public class HeapSnapshotResponse
{
  [Key(0)]
  public List<TypeStats> Types { get; set; }

  [Key(1)]
  public long TotalHeapSize { get; set; }

  [Key(2)]
  public long TotalObjectCount { get; set; }
}

[MessagePackObject]
public class TypeStats
{
  [Key(0)]
  public string TypeName { get; set; }

  [Key(1)]
  public long Count { get; set; }

  [Key(2)]
  public long TotalSize { get; set; }

  [Key(3)]
  public int Gen0Count { get; set; }

  [Key(4)]
  public int Gen1Count { get; set; }

  [Key(5)]
  public int Gen2Count { get; set; }

  [Key(6)]
  public int LohCount { get; set; }

  [Key(7)]
  public long Gen0Size { get; set; }

  [Key(8)]
  public long Gen1Size { get; set; }

  [Key(9)]
  public long Gen2Size { get; set; }

  [Key(10)]
  public long LohSize { get; set; }
}

[MessagePackObject]
public class RetainPathResponse
{
  [Key(0)]
  public List<RetainPath> Paths { get; set; }
}

[MessagePackObject]
public class RetainPath
{
  /// <summary>
  /// Classification of the root: "Static", "ThreadLocal", "Pinned",
  /// "Finalizer", or "Unknown".
  /// </summary>
  [Key(0)]
  public string RootKind { get; set; }

  [Key(1)]
  public List<RetainPathEntry> Entries { get; set; }
}

[MessagePackObject]
public class RetainPathEntry
{
  [Key(0)]
  public ulong Address { get; set; }

  [Key(1)]
  public string TypeName { get; set; }

  [Key(2)]
  public long Size { get; set; }

  /// <summary>
  /// The field on this object that references the next entry in the path.
  /// </summary>
  [Key(3)]
  public string FieldName { get; set; }
}

[MessagePackObject]
public class TypeInstancesResponse
{
  [Key(0)]
  public List<TypeInstance> Instances { get; set; }
}

[MessagePackObject]
public class TypeInstance
{
  [Key(0)]
  public ulong Address { get; set; }

  [Key(1)]
  public long Size { get; set; }

  [Key(2)]
  public int Generation { get; set; }
}

//
// Retain chain (on-demand, per-type)
//

[MessagePackObject]
public class RetainChainRequest
{
  [Key(0)]
  public string TypeName { get; set; }

  [Key(1)]
  public int MaxDepth { get; set; } = 8;
}

[MessagePackObject]
public class RetainChainResponse
{
  [Key(0)]
  public List<RetainPathEntry> Chain { get; set; }

  [Key(1)]
  public ulong SampleAddress { get; set; }
}

//
// Static holders (root-cause analysis by static field ownership)
//

[MessagePackObject]
public class StaticHoldersRequest
{
  [Key(0)]
  public int TopN { get; set; } = 50;
}

[MessagePackObject]
public class StaticHoldersResponse
{
  [Key(0)]
  public List<StaticHolder> Holders { get; set; }

  [Key(1)]
  public int TotalStaticRoots { get; set; }

  [Key(2)]
  public long TotalRetainedBytes { get; set; }
}

[MessagePackObject]
public class StaticHolder
{
  /// <summary>
  /// Declaring type of the static field (e.g. "WotC...PlayerEventManager").
  /// </summary>
  [Key(0)]
  public string HolderType { get; set; }

  /// <summary>
  /// Name of the static field (e.g. "s_instance", "m_openMatchesByEventId").
  /// </summary>
  [Key(1)]
  public string FieldName { get; set; }

  /// <summary>
  /// Address of the object referenced by the static field.
  /// </summary>
  [Key(2)]
  public ulong RootAddress { get; set; }

  /// <summary>
  /// Type of the object the static field points to.
  /// </summary>
  [Key(3)]
  public string RootTypeName { get; set; }

  /// <summary>
  /// Transitive sum of object sizes reachable from this static root,
  /// with shared subgraphs attributed to the first visiting root.
  /// </summary>
  [Key(4)]
  public long RetainedBytes { get; set; }

  /// <summary>
  /// Transitive count of objects reachable from this static root.
  /// </summary>
  [Key(5)]
  public int ObjectCount { get; set; }

  /// <summary>
  /// Type of the single largest reachable object (often the "culprit"
  /// collection or buffer).
  /// </summary>
  [Key(6)]
  public string DominantChildType { get; set; }

  /// <summary>
  /// Size of the single largest reachable object.
  /// </summary>
  [Key(7)]
  public long DominantChildSize { get; set; }
}
