/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Types;

public class DynamicRemoteEnumerator(dynamic remoteEnumerator)
  : IEnumerator<object>, IDisposable
{
  public object Current => remoteEnumerator.Current;

  public bool MoveNext() => remoteEnumerator.MoveNext();

  public void Reset() => remoteEnumerator.Reset();

  public void Dispose()
  {
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
