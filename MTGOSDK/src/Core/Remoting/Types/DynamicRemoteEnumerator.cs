/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Types;

public class DynamicRemoteEnumerator(dynamic remoteEnumerator)
  : IEnumerator<object>, IDisposable
{
  //
  // IEnumerator<object> implementation
  //

  public object Current => remoteEnumerator.Current;

  public bool MoveNext() => remoteEnumerator.MoveNext();

  public void Reset() => remoteEnumerator.Reset();

  private bool _isDisposed = false;

  //
  // IDisposable implementation
  //

  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    try
    {
      remoteEnumerator.Dispose();
    }
    catch
    {
      remoteEnumerator.Reset();
      remoteEnumerator.MoveNext();
    }
  }

  ~DynamicRemoteEnumerator()
  {
    Dispose();
  }
}
