/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using ImpromptuInterface;


namespace MTGOSDK.Core.Reflection.Proxy;

public class TypeProxy(Type? @type=null) : TypeProxy<dynamic>(@type)
{ }

public class TypeProxy<T>(Type? @type=null) where T : class
{
  //
  // BuilderProxy methods
  //

  /// <summary>
  /// Binds the proxied object to the specified interface type.
  /// </summary>
  /// <typeparam name="T">The interface type to bind to.</typeparam>
  /// <param name="obj">The remote object to bind to.</param>
  /// <returns>The proxied object.</returns>
  public static T As(dynamic? obj=null) =>
    Impromptu.ActLike<T>(obj);

  /// <summary>
  /// Binds the proxied object to the specified interface types.
  /// </summary>
  /// <remarks>
  /// The first interface type is used as the base interface.
  /// </remarks>
  /// <param name="obj">The remote object to bind to.</param>
  /// <param name="interfaces">The interface types to bind to.</param>
  /// <returns>The proxied object.</returns>
  public static dynamic As(dynamic? obj=null, params Type[] interfaces) =>
    Impromptu.DynamicActLike(obj, interfaces);

  /// <summary>
  /// Unbinds the proxied object from any bound interface types.
  /// </summary>
  /// <param name="obj">The remote object to unbind.</param>
  /// <returns>The unbound proxied object.</returns>
  public static dynamic From(dynamic? obj=null) =>
    Impromptu.UndoActLike(obj);

  /// <summary>
  /// Check whether an instance implements a proxy interface.
  /// </summary>
  /// <param name="obj">The object to check</param>
  /// <returns>True if the object is a proxy</returns>
  public static bool IsProxy(dynamic? obj=null) =>
    obj != null &&
    typeof(IActLikeProxy).IsAssignableFrom(obj.GetType());

  //
  // Derived class properties
  //

  /// <summary>
  /// Returns the type of the proxied class.
  /// </summary>
  public readonly Type Class = @type ?? typeof(T);

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
  /// Returns the assembly version of the proxied class.
  /// </summary>
  /// <remarks>
  /// This is the version of the local assembly that the proxied class wraps.
  /// </remarks>
  public string AssemblyVersion => Class.Assembly.GetName().Version.ToString();

  /// <summary>
  /// Returns true if the proxied class is static at the IL level.
  /// </summary>
  public bool IsStatic => Class.IsAbstract && Class.IsSealed;

  /// <summary>
  /// Whether the proxied class is an interface.
  /// </summary>
  public bool IsInterface => Class.IsInterface;

  //
  // Derived remote object properties
  //

  // /// <summary>
  // /// Returns true if the remote object is cached and pinned in client memory.
  // /// </summary>
  // public bool IsCached => obj != null;

  public override string ToString() => Class.FullName
    ?? throw new TypeInitializationException(
        $"The Proxied type is not a valid type.", innerException: null);

  public static implicit operator string(TypeProxy<T> proxy) =>
    proxy.ToString();
}
