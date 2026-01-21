# MTGOSDK.API.ObjectProvider

The [`ObjectProvider`](/MTGOSDK/src/API/ObjectProvider.cs) class is a static class that manages the retrieval of objects registered globally in MTGO, providing an easy way to access singleton objects. This is useful for managing objects that are frequently used throughout the application, and helps ensure that objects are properly managed and cleaned up when the client is closed.

For MTGO reference assembly types, we can use the **ObjectProvider** class to retrieve objects by using reference-type's fully qualified names as a unique identifier. Only objects that are registered with the ObjectProvider class inside the MTGO client can be retrieved this way.

## Retrieving Singleton Objects

Generally we can retrieve these objects using any of the following methods:

```C#
using MTGOSDK.API;                   // ObjectProvider
using MTGOSDK.Core.Reflection.Proxy; // TypeProxy<T>

IBar objA = ObjectProvider.Get<IBar>();
IBar objB = ObjectProvider.Get(new TypeProxy<IBar>());
IBar objB = ObjectProvider.Get(new TypeProxy<dynamic>(typeof(IBar)));
IBar objC = ObjectProvider.Get("assembly.namespace.IBar");
```

Here the [`TypeProxy<T>`](/MTGOSDK/src/Core/Reflection/Proxy.cs) type is used to convert the interface type to a string containing the fully qualified name of the interface type. You can also hard-code the string value if you know the fully qualified name ahead of time.

Under the hood, the `Get<T>` method will call the `Bind<T>()` method from [`DLRWrapper<T>`](../architecture/dlr-wrapper.md), but it will also cache the object for future use. This is demonstrated in the following example, which uses the `Get<T>` method to retrieve an object of type IBar.

```C#
using MTGOSDK.API;             // ObjectProvider
using MTGOSDK.Core.Reflection; // DLRWrapper<I>

public class Foo : DLRWrapper<Bar>
{
  internal override IBar obj = ObjectProvider.Get<IBar>();

  public static int    A => @base.A;
  public static string B => @base.B;
  public static string C =>
    Unbind(@base).C; // May not always work as expected, see below.
}
```

This will provide an optimized code-path for retrieving frequently used objects from the MTGO client's **ObjectProvider** class.

## Caveats

This method carries several type restrictions that may not always work with **DLRWrapper**'s `Unbind()` method, as the **ObjectProvider** class will cache the object type as the interface type, not the dynamic object type. This will persist in the reflection cache for the lifetime of the application, and may cause inconsistent behavior if the object is unbound and rebound to a different interface type.

Cases where this class fails will often result in a runtime exception reporting an incorrect type based on the Relative Virtual Address (RVA) of the type in the reflection cache. Such cases are rare, but can occur when the object is unbound and rebound to a different interface type, or when the object is bound to an interface type that is not compatible with the object's members.

A possible workaround is demonstrated in the following example, which retrieves an object from the **ObjectProvider** class without binding an interface type to the dynamic object.

```C#
using MTGOSDK.API;             // ObjectProvider
using MTGOSDK.Core.Reflection; // DLRWrapper<I>

public class Foo : DLRWrapper<Bar>
{
  internal override dynamic obj => ObjectProvider.Get<IBar>(bindTypes: false);

  public static int    A => @base.A;
  public static string B => @base.B;
  public static string C => @base.C; // May still conflict with reflection cache.
}
```

In such cases, it is best to limit the use of the [`ObjectProvider`](./object-provider.md) class to objects that have well-defined interface types, or objects that are not unbound and rebound to different interface types. Objects provided by [`RemoteClient`](../architecture/remote-client.md) will not attempt to bind interface types, though these objects may still face reflection cache conflicts if another instance is bound to an interface type.

It may help to provide the `DLRWrapper<T>` class with a reference to the bound type to yield more informative stack traces, as the `DLRWrapper<T>` class will attempt to resolve the type from the reflection cache if the type is not provided. Below demonstrates how to provide the `DLRWrapper<T>` class with a reference to the bound type by overriding the `type` property.

```C#
using MTGOSDK.API;             // ObjectProvider
using MTGOSDK.Core.Reflection; // DLRWrapper<I>

public class Foo(dynamic bar) : DLRWrapper<Bar>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IBar);

  /// <summary>
  /// Stores an internal reference to the IBar object.
  /// </summary>
  internal override dynamic obj => Bind<IBar>(bar);
}
```

Providing a separate reference to the underlying type will help the `DLRWrapper<T>` class ensure that the correct type is used when checking the results of type binding and unbinding operations. This will help prevent reflection cache conflicts and ensure that the correct type is used in cases that would otherwise result in a runtime exception.
