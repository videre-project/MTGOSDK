/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// Identifies an owner's capture stream and its native completion order.
/// Registration returns the current sequence as the subscriber's starting baseline.
/// </summary>
[MessagePackObject]
public sealed class RemoteCaptureSequence
{
  [Key(0)]
  public Guid StreamId { get; set; }

  [Key(1)]
  public long Sequence { get; set; }

  /// <summary>
  /// True when an enclosing native invocation completed this capture.
  /// </summary>
  [Key(2)]
  public bool Scoped { get; set; }

  /// <summary>
  /// The number of notifications retained by this native scope.
  /// </summary>
  [Key(3)]
  public int NotificationCount { get; set; }

  /// <summary>
  /// Native event names observed in the scope, for subscription maintenance.
  /// </summary>
  [Key(4)]
  public string[] EventNames { get; set; } = Array.Empty<string>();

  /// <summary>
  /// Identifies the source of a standalone event or requested capture.
  /// Scoped notification captures leave this zero.
  /// </summary>
  [Key(5)]
  public ulong SourceToken { get; set; }

  /// <summary>
  /// The owner's state revision, shared by its boundary and subsequent local captures.
  /// </summary>
  [Key(6)]
  public long StateRevision { get; set; }

  /// <summary>
  /// True for the capture produced by the revision's owning invocation.
  /// </summary>
  [Key(7)]
  public bool IsStateBoundary { get; set; }

  /// <summary>
  /// Links a boundary to its deferred notification capture. Zero means that the
  /// boundary did not produce a notification capture.
  /// </summary>
  [Key(8)]
  public long NotificationSequence { get; set; }

  /// <summary>
  /// Groups subscribed source tokens by event name so consumers can identify which
  /// sources contributed to the capture. Each source appears once per event name.
  /// </summary>
  [Key(9)]
  public Dictionary<string, ulong[]> EventSources { get; set; } = new();

  /// <summary>
  /// True when a caller requests the retained capture without a native notification.
  /// </summary>
  [Key(10)]
  public bool Requested { get; set; }
}
