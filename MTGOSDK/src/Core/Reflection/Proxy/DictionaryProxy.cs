/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;


namespace MTGOSDK.Core.Reflection.Proxy;

/// <summary>
/// Represents a proxy object for a remote dictionary object;
/// </summary>
public class DictionaryProxy<TKey, TValue>(
  dynamic dictionary,
  Func<dynamic, TKey>? keyMapper = null,
  Func<dynamic, TValue>? valueMapper = null)
    : DLRWrapper<IDictionary<TKey, TValue>>, IDictionary<TKey, TValue>
      where TKey : notnull
      where TValue : notnull
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IDictionary<TKey, TValue>);

  /// <summary>
  /// Stores an internal reference to the remote list object.
  /// </summary>
  internal override dynamic obj => Bind<IDictionary<TKey, TValue>>(dictionary);

  private readonly dynamic _keyTypeMapper =
    keyMapper ?? UseTypeMapper<dynamic, TKey>();

  private readonly dynamic _valueTypeMapper =
    valueMapper ?? UseTypeMapper<dynamic, TValue>();

  private bool CompareKeys(TKey key, dynamic remoteKey) =>
    Try(() => key.ToString() == remoteKey.ToString(),
        () => key.Equals(_keyTypeMapper(remoteKey)));

  private bool CompareValues(TValue value, dynamic remoteValue) =>
    Try(() => value.ToString() == remoteValue.ToString(),
        () => value.Equals(_valueTypeMapper(remoteValue)));

  /// <summary>
  /// Stores a reference to the remote keys collection.
  /// </summary>
  private readonly dynamic _remoteKeys =
    Map<IList, dynamic>(
      Try(Lambda(() => Unbind(dictionary).Keys), dictionary.Keys));

  /// <summary>
  /// Attempts to get the remote key object for the given key.
  /// </summary>
  /// <param name="key">The key to search for.</param>
  /// <param name="obj">The remote key object if found.</param>
  /// <returns>True if the key was found; otherwise, false.</returns>
  public bool TryGetRemoteKey(TKey key, out dynamic obj)
  {
    foreach (dynamic remoteKey in _remoteKeys)
    {
      if (CompareKeys(key, remoteKey))
      {
        obj = remoteKey;
        return true;
      }
    }

    obj = default;
    return false;
  }

  //
  // IDictionary<TKey, TValue> wrapper properties
  //

  public int Count => @base.Count;

  public bool IsReadOnly => true;

  public TValue this[TKey key]
  {
    get
    {
      if (!TryGetRemoteKey(key, out var remoteKey))
        throw new KeyNotFoundException(
          $"The key '{key}' was not found in the dictionary.");

      return _valueTypeMapper(Unbind(@base)[remoteKey]);
    }
    set
    {
      if (!TryGetRemoteKey(key, out var remoteKey))
        throw new KeyNotFoundException(
          $"The key '{key}' was not found in the dictionary.");

      Unbind(@base)[remoteKey] = value;
    }
  }

  public ICollection<TKey> Keys => Map<TKey>(@base.Keys, _keyTypeMapper);

  public ICollection<TValue> Values => Map<TValue>(@base.Values, _valueTypeMapper);

  //
  // IDictionary<TKey, TValue> wrapper methods
  //

  public void Add(TKey key, TValue value) =>
    throw new NotImplementedException();

  public void Add(KeyValuePair<TKey, TValue> item) =>
    throw new NotImplementedException();

  public bool Remove(TKey key) =>
    throw new NotImplementedException();

  public bool Remove(KeyValuePair<TKey, TValue> item) =>
    throw new NotImplementedException();

  public void Clear() =>
    throw new NotImplementedException();

  public bool ContainsKey(TKey key)
  {
    foreach (dynamic remoteKey in Unbind(@base))
    {
      if (CompareKeys(key, remoteKey))
        return true;
    }
    return false;
  }

  public bool TryGetValue(TKey key, out TValue value)
  {
    foreach (dynamic remoteKey in _remoteKeys)
    {
      if (CompareKeys(key, remoteKey))
      {
        value = _valueTypeMapper(Unbind(@base)[remoteKey]);
        return true;
      }
    }
    value = default;
    return false;
  }

  public bool Contains(KeyValuePair<TKey, TValue> item)
  {
    foreach (dynamic remoteKey in _remoteKeys)
    {
      if (CompareKeys(item.Key, remoteKey) &&
          CompareValues(item.Value, Unbind(@base)[remoteKey]))
        return true;
    }
    return false;
  }

  public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
  {
    foreach (dynamic remoteKey in _remoteKeys)
    {
      array[arrayIndex++] = new KeyValuePair<TKey, TValue>(
        _keyTypeMapper(remoteKey),
        _valueTypeMapper(Unbind(@base)[remoteKey]));
    }
  }

  public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
  {
    foreach (dynamic remoteKey in _remoteKeys)
    {
      yield return new KeyValuePair<TKey, TValue>(
        _keyTypeMapper(remoteKey),
        _valueTypeMapper(Unbind(@base)[remoteKey]));
    }
  }

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
