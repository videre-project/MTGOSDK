/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Compiler;


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
  /// Initializes a new instance of the <see cref="DLRWrapper{I}"/> class,
  /// executing any given factory function before any derived class constructors.
  /// </summary>
  /// <param name="factory">The factory function to execute (optional).</param>
  /// <remarks>
  /// This constructor is used to allow derived classes to override the type or
  /// instance of the wrapped object in a more flexible manner than possible
  /// through generics or constructor parameters.
  /// </remarks>
  public DLRWrapper(Action? factory = null) : this()
  {
    // Initializes a given factory function, if provided.
    if (factory != null) factory.Invoke();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="DLRWrapper{I}"/> class,
  /// executing any given factory function before any derived class constructors.
  /// </summary>
  /// <param name="factory">The factory function to execute (optional).</param>
  /// <remarks>
  /// This constructor is used to allow derived classes to override the type or
  /// instance of the wrapped object in a more flexible manner than possible
  /// through generics or constructor parameters.
  /// </remarks>
  public DLRWrapper(Func<Task>? factory = null) : this()
  {
    // Initializes a given factory function, if provided.
    if (factory != null) factory.Invoke().Wait();
  }

  //
  // Internal fields and properties for the wrapped object.
  //

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
  /// <remarks>
  /// Derived classes must override this property to capture dynamic objects.
  /// </remarks>
  internal virtual dynamic obj =>
    throw new ArgumentException(
        $"{nameof(DLRWrapper<I>)}.obj must capture a {type.Name} type.");

  /// <summary>
  /// Internal unwrapped reference to any captured dynamic objects.
  /// </summary>
  /// <remarks>
  /// This is used to extract dynamic objects passed from any derived
  /// classes, deferring any dynamic dispatching of class constructors.
  /// </remarks>
  internal virtual dynamic @base
  {
    get
    {
      // Attempt to extract the base object from the derived class.
      dynamic baseObj = Try(() => obj is DLRWrapper<I> ? obj.obj : obj)
        ?? throw new ArgumentException(
            $"{nameof(DLRWrapper<I>)} object has no valid {type.Name} type.");

      // Return a DynamicProxy wrapper with a default value, if present.
      if (DefaultAttribute.TryGetCallerAttribute(out var defaultAttribute))
      {
        DynamicProxy proxy = new(baseObj, defaultAttribute.Value);
        return Rebind(baseObj, proxy);
      }

      return baseObj;
    }
  }

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
    // Unbind any nested interface types before re-binding the object.
    if (TypeProxy<dynamic>.IsProxy(obj))
      obj = Unbind(obj);

    return TypeProxy<T>.As(obj)
      ?? throw new InvalidOperationException(
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
    // Return the object if it is not a proxy type.
    if (!TypeProxy<dynamic>.IsProxy(obj))
      return obj;

    var unbound_obj = TypeProxy<dynamic>.From(obj)
      ?? throw new InvalidOperationException(
          $"Unable to unbind types from {obj.GetType().Name}.");

    // Recursively unbind any nested interface types.
    if (TypeProxy<dynamic>.IsProxy(unbound_obj))
      return Unbind(unbound_obj);

    return unbound_obj;
  }

  /// <summary>
  /// Unbinds the given object instances from the proxied wrapper type.
  /// </summary>
  /// <param name="objs">The objects to unbind.</param>
  /// <returns>The unbound objects.</returns>
  /// <remarks>
  /// This method is a wrapper for the <see cref="Proxy{T}.From"/> method.
  /// </remarks>
  public static dynamic Unbind(dynamic [] objs)
  {
    var unbound_objs = new dynamic[objs.Length];
    for (var i = 0; i < objs.Length; i++)
      unbound_objs[i] = Unbind(objs[i]);

    return unbound_objs;
  }

  /// <summary>
  /// Rebinds the given object instance to the proxied wrapper type.
  /// </summary>
  /// <param name="baseObj">The base object to extract the binding type.</param>
  /// <param name="obj">The object to rebind.</param>
  /// <returns>The rebound object.</returns>
  public static dynamic Rebind(dynamic baseObj, dynamic obj)
  {
    // If the base object is a proxy type, rebind the new proxy instance.
    if (TypeProxy<I>.IsProxy(baseObj))
    {
      var bindingType = new TypeProxy<I>(baseObj.GetType());
      return TypeProxy<I>.As(obj, bindingType.Interface);
    }

    // Otherwise, no rebinding is necessary.
    return obj;
  }

  /// <summary>
  /// Attempts to cast the given object to the given type with various fallbacks.
  /// </summary>
  /// <typeparam name="T">The type to cast to.</typeparam>
  /// <param name="obj">The object to cast.</param>
  /// <returns>The casted object.</returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the given object cannot be cast to the given type.
  /// </exception>
  public static T Cast<T>(dynamic obj)
  {
    // Attempt to directly cast the object to the given type.
    try { return (T)obj ?? throw null; }
    catch { }

    if (typeof(T) == typeof(string))
    {
      return (T)(obj?.ToString() ?? throw new InvalidOperationException(
          $"Unable to cast {obj.GetType().Name} to {typeof(T).Name}."));
    }

    // Test using the RuntimeBinder to implicitly cast the object.
    try { T result = obj; return result; }
    catch { }

    // Fallback to parsing the object type from a string.
    try
    {
      var str = obj.ToString();
      var type = typeof(T);
      if (type.IsEnum)
      {
        return (T)Enum.Parse(type, str) ?? throw null;
      }
      else
      {
        return (T)(type
          .GetMethod("Parse", new [] { typeof(string) })
          ?.Invoke(null, new object[] { str })) ?? throw null;
      }
    }
    catch { }

    // Fallback to creating a new instance assuming a DLRWrapper type.
    try { return (T)(InstanceFactory.CreateInstance(typeof(T), obj)); }
    catch { }
    try { return (T)(InstanceFactory.CreateInstance(typeof(T), obj.ToString())); }
    catch { }

    // Return the object if it is already of the given type.
    if (typeof(T).FullName == obj.GetType().FullName) return obj;

    // Throw an exception if the object cannot be cast to the given type.
    throw new InvalidOperationException(
        $"Unable to cast {obj.GetType().Name} to {typeof(T).Name}.");
  }

  /// <summary>
  /// A type wrapper function method for safely executing a lambda function.
  /// </summary>
  public static Func<dynamic> Lambda(Func<dynamic> lambda) => lambda;

  /// <summary>
  /// Provides a default type mapper based on the given reference type.
  /// </summary>
  internal static dynamic UseTypeMapper<T1, T2>()
    where T1 : notnull
    where T2 : notnull
  {
    return new Func<dynamic, T2>((item) =>
      // Handle items based on an explicit constructor or fallback to casting.
      typeof(T2).GetConstructors().Length == 0
        ? Cast<T2>(item)
        : Cast<T2>(Try(
          () => InstanceFactory.CreateInstance(typeof(T2), item),
          () => item)));
  }

  /// <summary>
  /// Iterates over an iterator and runs a callback or constructor on each item.
  /// </summary>
  /// <typeparam name="E">The enumerable type to cast to.</typeparam>
  /// <typeparam name="T1">The element type to cast from.</typeparam>
  /// <typeparam name="T2">The element type to cast to.</typeparam>
  /// <param name="obj">The object or enumerable to iterate over.</param>
  /// <param name="func">The function to run for each item.</param>
  /// <returns>An enumerable of the function's output.</returns>
  public static IEnumerable<T2> Map<E, T1, T2>(dynamic obj, Func<T1, T2>? func)
    where E : IEnumerable
    where T1 : notnull
    where T2 : notnull
  {
    // Guard against null objects and return an empty enumerable.
    if (obj == null) yield break;

    dynamic mapper = func as dynamic ?? UseTypeMapper<T1, T2>();

    // Check if the object can support indexing
    if (Try<bool>(() => obj[0] != null))
    {
      int count = Try(() => obj.Count, () => obj.Length);
      for (var i = 0; i < count; i++)
        yield return mapper(obj[i]);
    }
    // Otherwise, iterate using the object's enumerator
    else
    {
      foreach (var item in Cast<E>(obj))
        yield return mapper(item);
    }
  }

  /// <summary>
  /// Iterates over an iterator and runs a callback or constructor on each item.
  /// </summary>
  /// <typeparam name="T">The element type to cast to.</typeparam>
  /// <param name="obj">The object or enumerable to iterate over.</param>
  /// <param name="func">The function to run for each item (optional).</param>
  /// <returns>An enumerable of the function's output.</returns>
  public static IEnumerable<T> Map<T>(dynamic obj, Func<dynamic, T>? func = null)
    where T : notnull
  {
    return Map<IEnumerable, dynamic, T>(obj, func);
  }

  /// <summary>
  /// Iterates over a list and runs a callback or constructor on each item.
  /// </summary>
  /// <typeparam name="L">The list type to cast to.</typeparam>
  /// <typeparam name="T">The item type to cast to.</typeparam>
  /// <param name="obj">The object or enumerable to iterate over.</param>
  /// <param name="func">The function to run for each item (optional).</param>
  /// <param name="proxy">Whether to return a proxy instance (optional).</param>
  /// <returns>A list of the function's output.</returns>
  public static IList<T> Map<L, T>(
    dynamic obj,
    Func<dynamic, T>? func = null,
    bool proxy = false)
      where L : IList
      where T : notnull
  {
    dynamic innerList = Try(
      // Attempt to cast the object to a list type.
      () => Cast<IList>(obj),
      () => Cast<IList>(Unbind(obj)),
      // Otherwise fallback to a dynamic list implementation.
      () => obj);

    // // If `T` is a DLRWrapper type and the object is a dynamic remote object,
    // // then simply return a ListProxy instance wrapping the remote list object.
    // if (typeof(T).IsOpenSubtypeOf(typeof(DLRWrapper<>)))
    // {
    //   IList<T> proxy = new ListProxy<T>innerList, func);
    //
    //   // If the instance has a well-defined count property, return the instance.
    //   if (Try<bool>(() => proxy.Count >= 0))
    //     return proxy;
    // }
    if (proxy) return new ListProxy<T>(innerList, func);

    // Otherwise allocate a local list object and map the items to the new type.
    IList<T> newList = Try(
      // Attempt to create a new instance of the 'L' list type.
      () => InstanceFactory.CreateInstance(typeof(L)),
      // Otherwise fallback to a generic list implementation
      // (i.e. when the provided type is abstract or has no constructor).
      () => new List<T>());

    foreach (var item in Map<T>(innerList, func))
      newList.Add(item);

    return newList;
  }

  /// <summary>
  /// Represents a method that defines a set of criteria and determines whether
  /// the specified object in an iterable meets those criteria.
  /// </summary>
  /// <param name="obj">The object to test against the criteria.</param>
  /// <returns>true if the object meets the criteria; otherwise, false.</returns>
  public delegate bool Predicate(dynamic obj);

  /// <summary>
  /// Filters a collection of dynamic objects based on a given predicate.
  /// </summary>
  /// <param name="obj">The collection of dynamic objects to filter.</param>
  /// <param name="predicate">The predicate used to filter the objects.</param>
  /// <returns>An enumerable collection of dynamic objects that satisfy the predicate.</returns>
  public static IEnumerable<dynamic> Filter(dynamic obj, Predicate predicate)
  {
    foreach (var item in obj)
      if (predicate(item)) yield return item;
  }

  //
  // Wrapper methods for safely retrieving properties or invoking methods.
  //

  /// <summary>
  /// Safely executes each lambda function until one succeeds.
  /// </summary>
  /// <param name="lambdas">The functions to execute in order.</param>
  /// <returns>The result of the function or the fallback value.</returns>
  public static dynamic Try(params Func<dynamic>[] lambdas)
  {
    foreach (var lambda in lambdas)
    {
      try { return lambda(); } catch { }
    }
    return null;
  }

  /// <summary>
  /// Safely executes a lambda function and returns the result or a fallback.
  /// </summary>
  /// <param name="lambda">The function to execute.</param>
  /// <param name="fallback">The fallback value to return (optional).</param>
  /// <returns>The result of the function or the fallback value.</returns>
  public static dynamic Try(Func<dynamic> lambda, dynamic fallback = null) =>
    Try(lambda, () => fallback);

  /// <summary>
  /// Safely executes a lambda function and returns the result or a fallback.
  /// </summary>
  /// <typeparam name="T">The result type to use or fallback to.</typeparam>
  /// <param name="lambda">The function to execute.</param>
  /// <returns>The result of the function or the fallback value.</returns>
  public static dynamic Try<T>(Func<dynamic> lambda) => Try(lambda, default(T));

  /// <summary>
  /// Safely executes a lambda function with a given number of retries.
  /// </summary>
  /// <param name="lambda">The function to execute.</param>
  /// <param name="delay">The delay in ms between retries (optional).</param>
  /// <param name="retries">The number of times to retry (optional).</param>
  /// <returns>True if the function executed successfully.</returns>
  public static async Task<bool> WaitUntil(
    Func<bool> lambda,
    int delay = 250,
    int retries = 20)
  {
    for (; retries > 0; retries--)
    {
      try { if (lambda()) return true; } catch { }
      await Task.Delay(delay);
    }
    return false;
  }

  /// <summary>
  /// Safely executes a lambda function with a given number of retries.
  /// </summary>
  /// <param name="lambda">The function to execute.</param>
  /// <param name="default">The default value to fallback to (optional).</param>
  /// <param name="delay">The delay in ms between retries (optional).</param>
  /// <param name="retries">The number of times to retry (optional).</param>
  /// <returns>The result of the function, otherwise an exception is thrown.</returns>
  public static T Retry<T>(
    Func<T> lambda,
    T @default = default(T),
    int delay = 100,
    int retries = 3,
    bool raise = false)
  {
    while (true)
    {
      try { return lambda(); }
      catch
      {
        retries--;
        if (retries <= 0)
        {
          if (raise) throw;
          return @default;
        }
        // This will block the caller's thread for the duration of the delay.
        Task.Delay(delay).Wait();
      }
    }
  }

  /// <summary>
  /// Marks the retrieval of a DLRWrapper's instance as optional.
  /// </summary>
  /// <typeparam name="T">The class type to instantiate.</typeparam>
  /// <param name="obj">The object to wrap.</param>
  /// <param name="condition">The condition to check before wrapping.</param>
  /// <returns>The wrapped object or null if the object is null.</returns>
  public static T? Optional<T>(
      dynamic obj,
      Func<dynamic, bool> condition = null) where T : class
  {
    // Return null if the condition is not met
    if (condition != null && !condition(obj))
      return null;

    // Return null if the underlying object is null
    if (Try<bool>(() => obj == null || Unbind(obj) == null))
      return null;

    if (typeof(T).IsSubclassOf(typeof(DLRWrapper<I>)))
      return (T)InstanceFactory.CreateInstance(typeof(T), obj);
    else
      return Cast<T>(obj);
  }

  /// <summary>
  /// Marks the retrieval of a DLRWrapper's instance as optional.
  /// </summary>
  /// <typeparam name="T">The class type to instantiate.</typeparam>
  /// <param name="obj">The object to wrap.</param>
  /// <param name="condition">The condition to check before wrapping.</param>
  /// <returns>The wrapped object or null if the object is null.</returns>
  public static T? Optional<T>(dynamic obj, bool condition) where T : class
  {
    return Optional<T>(obj, new Func<dynamic, bool>(_ => condition));
  }
}
