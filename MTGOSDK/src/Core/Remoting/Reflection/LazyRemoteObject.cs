/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting.Reflection;

/// <summary>
/// A wrapper for lazy initializing dynamic remote objects.
/// </summary>
public class LazyRemoteObject : DynamicRemoteObject
{
  public override RemoteHandle __ra => @instance?.Value?.__ra!;
  public override RemoteObject __ro => @instance?.Value?.__ro!;
  public override RemoteType __type => @instance?.Value?.__type!;

  /// <summary>
  /// The lazy object to defer initialization of the remote object.
  /// </summary>
  private Lazy<dynamic> @instance = null!;

  /// <summary>
  /// Callback to reset the lazy initializer for the created instance.
  /// </summary>
  public delegate dynamic TResetter(Func<dynamic> callback);

  /// <summary>
  /// Sets the lazy initializer function for the remote object.
  /// </summary>
  /// <param name="c">The lazy initializer function.</param>
  /// <returns>A reference to this function.</returns>
  public TResetter Set(Func<dynamic>? callback)
  {
    @instance = callback != null ? new(() => callback!.Invoke()) : null!;
    return new TResetter(c => Set(c));
  }
}
