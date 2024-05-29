/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;


namespace MTGOSDK.Core.Reflection;
using static Attributes;

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
      var baseObj = obj is DLRWrapper<I> ? obj.obj : obj
        ?? throw new ArgumentException(
            $"{nameof(DLRWrapper<I>)} object has no valid {type.Name} type.");

      // Return a ProxyObject wrapper with a default value, if present.
      if (TryGetDefaultAttribute(out var defaultAttribute))
        return new ProxyObject(baseObj, @default: defaultAttribute.Value);

      return baseObj;
    }
  }

  /// <summary>
  /// Internal reference to the remote object handle.
  /// </summary>
  internal dynamic @ro => Try(() => Unbind(@base).__ro, () => @base.__ro)
    ?? throw new InvalidOperationException(
        $"{type.Name} type does not implement RemoteObject.");

  //
  // Deferred remote object initialization.
  //

  /// <summary>
  /// Internal queue for deferring remote object initialization.
  /// </summary>
  internal static ConcurrentQueue<dynamic> DeferedQueue = new();

  /// <summary>
  /// Initializes and constructs any deferred remote objects in the queue.
  /// </summary>
  /// <param name="_ref">
  /// An optional reference to a deferred object from a derived or base class.
  /// This is used to ensure that the queue is not empty when calling the class
  /// constructor, triggering the lazy initialization of any deferred static
  /// fields for immediate use.
  /// </param>
  /// <param name="raise">
  /// Whether to raise an exception if the queue is empty (optional).
  /// </param>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the queue is empty and no deferred objects are available.
  /// </exception>
  public static void Construct(dynamic? _ref = null, bool raise = false)
  {
    // Use any dereferenced object to lazy initialize and check the queue.
    if (_ref is not null && (raise && DeferedQueue.IsEmpty))
      throw new InvalidOperationException(
          $"{nameof(DLRWrapper<I>)}.Construct() called with no deferred objects.");

    // Initializes any deferred remote objects in the queue.
    while (DeferedQueue.TryDequeue(out var proxy))
    {
      proxy.Construct();
    }
  }

  /// <summary>
  /// Defers the execution of a function or method group until access is needed.
  /// </summary>
  /// <typeparam name="T">The type or interface to return.</typeparam>
  /// <param name="c">The function or method group to defer execution.</param>
  /// <returns>A proxied remote object that can be lazily initialized.</returns>
  public static T Defer<T>(Func<T> c) where T : class
  {
    // Enqueue the proxy object to be initialized later.
    var refProxy = new RemoteProxy<T>(c);
    DeferedQueue.Enqueue(refProxy);

    // Defer a proxy object bound to the remote object handle.
    T proxy = null!;
    refProxy.Defer(ref proxy);

    return proxy;
  }

  /// <summary>
  /// Swaps the dynamic remote object handle with another instance.
  /// </summary>
  /// <param name="refObj">The reference object to swap.</param>
  /// <param name="obj">The object to swap with.</param>
  /// <param name="bindTypes">Whether to bind reference types (optional).</param>
  /// <returns>The reference object with the swapped remote object handle.</returns>
  public static dynamic Swap(
    ref dynamic refObj,
    dynamic obj,
    bool bindTypes = false)
  {
    // Extract the remote object handle from the source object.
    var objDro = bindTypes ? obj : Unbind(obj);

    // Copy the remote object handle to the reference object.
    refObj.__ra   = objDro.__ra;
    refObj.__ro   = objDro.__ro;
    refObj.__type = objDro.__type;

    return refObj;
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
    return Proxy<T>.As(obj)
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
    var unbound_obj = Proxy<dynamic>.From(obj)
      ?? throw new InvalidOperationException(
          $"Unable to unbind types from {obj.GetType().Name}.");

    // Recursively unbind any nested interface types.
    if (unbound_obj.GetType().Name.StartsWith("ActLike_"))
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

    // Throw an exception if the object cannot be cast to the given type.
    throw new InvalidOperationException(
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

  //
  // Wrapper methods for safely retrieving properties or invoking methods.
  //

  /// <summary>
  /// Safely executes a lambda function and returns the result or a fallback.
  /// </summary>
  /// <param name="lambda">The function to execute.</param>
  /// <param name="fallback">The fallback value to return (optional).</param>
  /// <returns>The result of the function or the fallback value.</returns>
  public static dynamic Try(Func<dynamic> lambda, dynamic fallback = null)
  {
    try { return lambda(); } catch { return fallback; }
  }

  /// <summary>
  /// Safely executes a lambda function and returns the result or a fallback.
  /// </summary>
  /// <param name="lambda">The function to execute.</param>
  /// <param name="fallback">The fallback function to execute.</param>
  /// <returns>The result of the function or the fallback value.</returns>
  public static dynamic Try(Func<dynamic> lambda, Func<dynamic> fallback)
  {
    try { return lambda(); } catch { return Try(fallback); }
  }

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
  /// <param name="delay">The delay in ms between retries (optional).</param>
  /// <param name="retries">The number of times to retry (optional).</param>
  /// <returns>The result of the function, otherwise an exception is thrown.</returns>
  public static async Task<dynamic> TryUntil(
    Func<dynamic> lambda,
    int delay = 250,
    int retries = 20)
  {
    while (true)
    {
      try { return lambda(); }
      catch
      {
        retries--;
        if (retries <= 0) throw;
        await Task.Delay(delay);
      }
    }
  }

  //
  // Wrapper Attributes
  //

  /// <summary>
  /// A wrapper attribute that allows for a default value to fallback to.
  /// </summary>
  /// <param name="value">The default value.</param>
  public class DefaultAttribute(object value) : Attribute
  {
    public object Value { get; set; } = value;
  }

  /// <summary>
  /// Determines whether the type is compiler-generated.
  /// </summary>
  public static bool IsCompilerGenerated(Type t)
  {
    if (t == null) return false;

    return t.IsDefined(typeof(CompilerGeneratedAttribute), false)
      || IsCompilerGenerated(t.DeclaringType);
  }


  //
  // Attribute wrapper helpers.
  //

  /// <summary>
  /// Gets the parent type of the caller.
  /// </summary>
  /// <param name="depth">The stack frame depth.</param>
  /// <returns>The type of the caller.</returns>
  public static Type GetCallerType(int depth = 4) =>
    new StackFrame(depth).GetMethod().ReflectedType;

  /// <summary>
  /// Gets the stack frame depth of the (outer) DLRWrapper caller.
  /// </summary>
  /// <param name="depth">The starting stack frame depth.</param>
  /// <returns>The caller's stack frame depth.</returns>
  private static int GetCallerDepth(int depth = 3)
  {
    Type wrapperType = GetCallerType(depth);
    while(GetCallerType(depth).Name == wrapperType.Name && depth < 50) depth++;

    return depth;
  }

  /// <summary>
  /// Attempts to get the default attribute from the (outer) DLRWrapper caller.
  /// </summary>
  /// <param name="attribute">The default attribute (if present).</param>
  /// <returns>True if the default attribute was found.</returns>
  private static bool TryGetDefaultAttribute(out DefaultAttribute? attribute)
  {
    attribute = GetCallerAttribute<DefaultAttribute>(depth: GetCallerDepth());
    return attribute != null;
  }
}
