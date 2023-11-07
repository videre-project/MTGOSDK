/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// A wrapper for dynamic objects that implement an interface at runtime.
/// </summary>
/// <remarks>
/// This class exposes an overrideable <see cref="obj"/> property that is used
/// to capture dynamic objects passed to the constructor. This allows derived
/// classes to defer dynamic dispatching of class constructors until after
/// the base class constructor has completed, exposing the captured dynamic
/// object to derived classes with the <see cref="@base"/> property.
/// </remarks>
/// <typeparam name="I">The interface type to wrap.</typeparam>
public class DLRWrapper<I>() where I : class
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  /// <remarks>
  /// This is used to allow derived classes to override the type of the
  /// wrapped object in a more flexible manner than using generics.
  /// </remarks>
  internal virtual Type type => typeof(I);

  /// <summary>
  /// This is the internal reference for any dynamic or derived class objects.
  /// </summary>
  internal virtual dynamic obj =>
    //
    // Derived classes must override this property to capture dynamic objects.
    //
    throw new Exception(
        $"{nameof(DLRWrapper<I>)}.obj must capture a {type.Name} type.");

  /// <summary>
  /// Internal unwrapped reference to any captured dynamic objects.
  /// </summary>
  /// <remarks>
  /// This is used to extract dynamic objects passed from any derived
  /// classes, deferring any dynamic dispatching of class constructors.
  /// </remarks>
  internal dynamic @base => obj is DLRWrapper<I> ? obj.obj : obj
    ?? throw new Exception(
        $"{nameof(DLRWrapper<I>)} object has no valid {type.Name} type.");

  //
  // Wrapper methods for type casting and dynamic dispatching.
  //

  /// <summary>
  /// Binds the given proxied wrapper type to an object instance.
  /// </summary>
  /// <typeparam name="T">The type to bind to.</typeparam>
  /// <param name="obj">The object to bind.</param>
  /// <returns>The bound object.</returns>
  /// <remarks>
  /// This method is a wrapper for the <see cref="Proxy{T}.As"/> method.
  /// </remarks>
  public static T Bind<T>(dynamic obj) where T : class
  {
    return Proxy<T>.As(obj)
      ?? throw new Exception(
          $"Unable to bind {obj.GetType().Name} to {typeof(T).Name}.");
  }

  /// <summary>
  /// Unbinds the given object instance from the proxied wrapper type.
  /// </summary>
  /// <param name="obj">The object to unbind.</param>
  /// <returns>The unbound object.</returns>
  /// <remarks>
  /// This method is a wrapper for the <see cref="Proxy{T}.From"/> method.
  /// </remarks>
  public static dynamic Unbind(dynamic obj)
  {
    var unbound_obj = Proxy<dynamic>.From(obj)
      ?? throw new Exception(
          $"Unable to unbind types from {obj.GetType().Name}.");

    // Recursively unbind any nested interface types.
    if (unbound_obj.GetType().Name.StartsWith("ActLike_"))
      return Unbind(unbound_obj);

    return unbound_obj;
  }

  /// <summary>
  /// Attempts to cast the given object to the given type with various fallbacks.
  /// </summary>
  /// <typeparam name="T">The type to cast to.</typeparam>
  /// <param name="obj">The object to cast.</param>
  /// <returns>The casted object.</returns>
  /// <exception cref="Exception">
  /// Thrown when the given object cannot be cast to the given type.
  /// </exception>
  public static T Cast<T>(dynamic obj)
  {
    // Attempt to directly cast the object to the given type.
    try { return (T)obj ?? throw new Exception(); }
    catch { }

    // Fallback to parsing the object type from a string.
    try
    {
      var str = obj.ToString();
      var type = typeof(T);
      if (type.IsEnum)
      {
        return (T)Enum.Parse(type, str)
          ?? throw new Exception();
      }
      else
      {
        return type.GetMethod("Parse")?.Invoke(null, str)
          ?? throw new Exception();
      }
    }
    catch { }

    // Throw an exception if the object cannot be cast to the given type.
    throw new Exception(
        $"Unable to cast {obj.GetType().Name} to {typeof(T).Name}.");
  }

  /// <summary>
  /// Iterates over an object and runs a callback or type constructor on each item.
  /// </summary>
  /// <typeparam name="T">The type to cast to.</typeparam>
  /// <param name="obj">The object or enumerable to iterate over.</param>
  /// <param name="func">The function to run for each item (optional).</param>
  /// <returns>An enumerable of the function's output.</returns>
  public static IEnumerable<T> Map<T>(dynamic obj, Func<dynamic, T>? func = null)
  {
    func ??= new Func<dynamic, T>((item) =>
        Cast<T>(InstanceFactory.CreateInstance(typeof(T), item)));
    foreach (var item in obj) yield return func(item);
  }
}
