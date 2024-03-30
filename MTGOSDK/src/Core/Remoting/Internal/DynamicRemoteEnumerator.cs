/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Collections;


namespace RemoteNET.Internal;

public class DynamicRemoteEnumerator(dynamic remoteEnumerator) : IEnumerator
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
      // TODO: Handle IDisposable's Dispose method explicitly when expected
      // by a using statement or a call to Dispose().
    }
  }
}
