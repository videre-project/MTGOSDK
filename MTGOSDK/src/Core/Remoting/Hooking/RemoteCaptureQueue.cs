/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace MTGOSDK.Core.Remoting.Hooking;

/// <summary>
/// Retains completed captures until all preceding entries in their native stream arrive.
/// The caller supplies a registration baseline and serializes access to this queue.
/// </summary>
public sealed class RemoteCaptureQueue<T>
{
  private readonly SortedDictionary<long, T> _pending = new();
  private Guid _streamId;
  private long _sequence;

  /// <summary>
  /// Number of later entries retained behind a native sequence gap.
  /// </summary>
  public int PendingCount => _pending.Count;

  /// <summary>
  /// The next completion required before later entries can be applied.
  /// </summary>
  public long NextSequence => _sequence + 1;

  /// <summary>
  /// Begins an owner attachment at its explicit native registration baseline.
  /// </summary>
  public void Reset(RemoteCaptureSequence baseline)
  {
    if (baseline is null) throw new ArgumentNullException(nameof(baseline));
    if (baseline.StreamId == Guid.Empty || baseline.Sequence < 0)
      throw new ArgumentException("Invalid capture stream baseline.", nameof(baseline));
    _pending.Clear();
    _streamId = baseline.StreamId;
    _sequence = baseline.Sequence;
  }

  /// <summary>
  /// Whether the capture belongs to the current attachment.
  /// </summary>
  public bool Matches(RemoteCaptureSequence sequence) =>
    sequence is not null && _streamId != Guid.Empty && sequence.StreamId == _streamId;

  /// <summary>
  /// Returns entries once every preceding sequence has arrived. Later entries wait
  /// behind a gap, and entries already applied or queued are ignored.
  /// </summary>
  public IReadOnlyList<T> Add(RemoteCaptureSequence sequence, T value)
  {
    if (!Matches(sequence))
      throw new ArgumentException("Capture belongs to another stream.", nameof(sequence));
    if (sequence.Sequence <= 0)
      throw new ArgumentException("Completed capture sequence must be positive.", nameof(sequence));
    if (sequence.Sequence <= _sequence || _pending.ContainsKey(sequence.Sequence))
      return Array.Empty<T>();
    _pending.Add(sequence.Sequence, value);
    var ready = new List<T>();
    while (_pending.TryGetValue(_sequence + 1, out var next))
    {
      _pending.Remove(++_sequence);
      ready.Add(next);
    }
    return ready;
  }
}
