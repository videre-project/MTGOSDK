/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Internal;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// A wrapper for lazy initializing typed references to remote objects.
/// </summary>
public sealed class RemoteProxy<I>(Func<I> c) : DLRWrapper<I>() where I : class
{
  /// <summary>
  /// Converts the captured member group to a typed delegate.
  /// </summary>
#if !NETSTANDARD2_0
  internal override Lazy<I> obj => new(() => c.Invoke());
#endif

  private object refLock = new();
  private dynamic refObj = new DynamicRemoteObject();

  internal override dynamic @base
  {
    get
    {
      lock (refLock)
      {
        if (refObj.__ro is null) Construct();
        return refObj;
      }
    }
  }

  /// <summary>
  /// Constructs the remote object handle from the lazy object.
  /// </summary>
  /// <exception cref="ArgumentException">
  /// Thrown when the object returned by the lazy object has an invalid type.
  /// </exception>
  internal void Construct()
  {
    lock (refLock)
    {
      // Extract the remote object handle from the lazy object.
      var dro = Unbind(obj.Value);

      // Copy the remote object handle to the reference object.
      refObj.__ra   ??= dro.__ra;
      refObj.__ro   ??= dro.__ro;
      refObj.__type ??= dro.__type;
    }
  }

  /// <summary>
  /// Defers the creation of a remote object handle to a later time.
  /// </summary>
  /// <param name="value">The reference to the remote object instance.</param>
  public void Defer(ref I value)
  {
    lock(refLock)
    {
      // Pass a reference to the remote object handle.
      try { value = Bind<I>(refObj); } catch { value = refObj; /* No type */ }
    }
  }

  public static implicit operator I(RemoteProxy<I> proxy) => Bind<I>(proxy.@base);
}
