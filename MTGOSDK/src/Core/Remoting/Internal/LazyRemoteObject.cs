/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Internal.Reflection;


namespace MTGOSDK.Core.Remoting.Internal;

/// <summary>
/// A wrapper for lazy initializing dynamic remote objects.
/// </summary>
public class LazyRemoteObject : DynamicRemoteObject
{
  public override RemoteHandle __ra => __ctor?.Value?.__ra!;
  public override RemoteObject __ro => __ctor?.Value?.__ro!;
  public override RemoteType __type => __ctor?.Value?.__type!;

  /// <summary>
  /// The lazy object to defer initialization of the remote object.
  /// </summary>
  private Lazy<dynamic> __ctor = null!;

  /// <summary>
  /// Sets the lazy initializer function for the remote object.
  /// </summary>
  /// <param name="c">The lazy initializer function.</param>
  /// <returns>A reference to this function.</returns>
  public Func<Func<dynamic>, dynamic> Set(Func<dynamic>? c1)
  {
    __ctor = c1 != null ? new(() => c1!.Invoke()) : null!;
    return new Func<Func<dynamic>, dynamic>(c => Set(c));
  }

  /// <summary>
  /// Sets the lazy object for the remote object.
  /// </summary>
  /// <param name="c">The lazy object.</param>
  /// <returns>A reference to this function.</returns>
  public Func<Lazy<dynamic>, dynamic> Set(Lazy<dynamic>? c2)
  {
    __ctor = c2!;
    return new Func<Lazy<dynamic>, dynamic>(c => Set(c));
  }
}
