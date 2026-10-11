/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// Compares values materialized from batch results. Dictionary entries are stored
/// as arrays, so their equality and hash codes must ignore entry order.
/// </summary>
internal sealed class CapturedValueComparer : IEqualityComparer<object?>
{
  internal static CapturedValueComparer Instance { get; } = new();
  private static readonly EntryComparer s_entries = new();

  private sealed class EntryComparer : IEqualityComparer<KeyValuePair<object?, object?>>
  {
    public bool Equals(KeyValuePair<object?, object?> left, KeyValuePair<object?, object?> right) =>
      Instance.Equals(left.Key, right.Key) && Instance.Equals(left.Value, right.Value);

    public int GetHashCode(KeyValuePair<object?, object?> pair) =>
      HashCode.Combine(Instance.GetHashCode(pair.Key), Instance.GetHashCode(pair.Value));
  }

  public new bool Equals(object? left, object? right)
  {
    if (ReferenceEquals(left, right))
      return true;
    if (left == null || right == null)
      return false;
    if (left is KeyValuePair<object?, object?>[] dictionary)
      return right is KeyValuePair<object?, object?>[] otherDictionary &&
        dictionary.Length == otherDictionary.Length &&
        new HashSet<KeyValuePair<object?, object?>>(dictionary, s_entries).SetEquals(otherDictionary);
    if (left is IEnumerable sequence && left is not string)
      return right is IEnumerable otherSequence && right is not string &&
        sequence.Cast<object?>().SequenceEqual(otherSequence.Cast<object?>(), this);
    return left.Equals(right);
  }

  public int GetHashCode(object? value)
  {
    if (value == null)
      return 0;
    if (value is KeyValuePair<object?, object?>[] dictionary)
    {
      int hash = 0;
      foreach (var entry in dictionary)
        hash ^= s_entries.GetHashCode(entry);
      return HashCode.Combine(dictionary.Length, hash);
    }
    if (value is IEnumerable sequence && value is not string)
    {
      var hash = new HashCode();
      foreach (var item in sequence)
        hash.Add(GetHashCode(item));
      return hash.ToHashCode();
    }
    return value.GetHashCode();
  }
}
