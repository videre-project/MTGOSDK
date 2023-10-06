/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core;

public class Proxy<@class>(
  dynamic? obj=null,
  Type? @type=null
) where @class : class {
  //
  // Derived class properties
  //

  /// <summary>
  /// Returns the type of the proxied class.
  /// </summary>
  public readonly Type Class = @type ?? typeof(@class);

  /// <summary>
  /// Returns the base class of the proxied class.
  /// </summary>
  public Type? Base => Class?.BaseType;

  /// <summary>
  /// Returns the first interface implemented by the proxied class.
  /// </summary>
  public Type? Interface {
    get {
      // Subtract the base class interfaces from the derived class interfaces
      var interfaces = Class.GetInterfaces().ToHashSet();
      if (Base != null && Base != typeof(object))
        interfaces.ExceptWith(Base.GetInterfaces().ToHashSet());

      return interfaces.First();
    }
  }

  /// <summary>
  /// Returns true if the proxied class is static at the IL level.
  /// </summary>
  public bool IsStatic => Class.IsAbstract && Class.IsSealed;

  //
  // Derived remote object properties
  //

  // /// <summary>
  // /// Returns true if the remote object is cached and pinned in client memory.
  // /// </summary>
  // public bool IsCached => obj != null;

  public override string ToString() => Class.FullName
    ?? throw new Exception($"Proxied type is not a valid type.");

  public static implicit operator string(Proxy<@class> proxy) =>
    proxy.ToString();
}
