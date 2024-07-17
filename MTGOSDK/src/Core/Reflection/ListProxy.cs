/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Represents a proxy object for a remote list object.
/// </summary>
public class ListProxy<T>(
  dynamic list,
  Func<dynamic, T>? func = null)
    : DLRWrapper<IList<T>>, IList<T> where T : notnull
{
  /// <summary>
  /// Stores an internal reference to the remote list object.
  /// </summary>
  internal override dynamic obj => Bind<IList>(list);

  private readonly dynamic _typeMapper = func ?? UseTypeMapper<dynamic, T>();

  //
  // IList<T> wrapper properties
  //

  public int Count => @base.Count;

  public bool IsReadOnly => @base.IsReadOnly;

  public T this[int index]
  {
    get => _typeMapper(Unbind(@base)[index]);
    set => Unbind(@base)[index] = value;
  }

  //
  // IList<T> wrapper methods
  //

  public void Add(T item) => @base.Add(item);

  public void Clear() => @base.Clear();

  public bool Contains(T item) => @base.Contains(item);

  public void CopyTo(T[] array, int arrayIndex)
  {
    var baseRef = Unbind(@base);
    for (int i = 0; i < this.Count; i++)
    {
      array[arrayIndex + i] = _typeMapper(baseRef[i]);
    }
  }

  public IEnumerator<T> GetEnumerator() => Map<T>(@base, func);

  public int IndexOf(T item) => @base.IndexOf(item);

  public void Insert(int index, T item) => @base.Insert(index, item);

  public bool Remove(T item) => @base.Remove(item);

  public void RemoveAt(int index) => @base.RemoveAt(index);

  //
  // IEnumerable wrapper methods
  //

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
