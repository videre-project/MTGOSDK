# MTGOSDK.Core.Remoting.RemoteClient

[`RemoteClient`](/MTGOSDK/src/Core/Remoting/RemoteClient.cs) provides static entry points for starting MTGO and accessing a shared connection to the client. Remote objects are exposed through `DynamicRemoteObject` proxies, which forward reflection and method calls to the Diver running in MTGO.

Below is a UML diagram of the RemoteClient class, which contains methods for creating, retrieving, and invoking remote objects. For now, we'll focus only on a subset of APIs for fetching and creating remote objects.

```mermaid
classDiagram
direction LR

  namespace MTGO {
    class ClrMD {
      ScubaDiver
    }
    class `MainUI (Primary AppDomain)` {
      UI Thread
      Render Loop - Background
      GC Thread - Background
      JIT Compiler Thread - CLR
      Dispatcher(UI)
    }
    class ConcreteType {
      +foo
      -bar
      +baz()
      -quz()
    }
  }
  ClrMD <-- `MainUI (Primary AppDomain)`: Snapshot Runtime
  ConcreteType <..> `MainUI (Primary AppDomain)`: .NET (Primary AppDomain)
  ClrMD <--> ConcreteType: Reflection
  ClrMD <-- RemoteClient: GetRemoteHandle()
  ClrMD <..> DynamicRemoteObject: TCP

  namespace MTGOSDK {
    class RemoteClient {
      @client: RemoteHandle
      -GetRemoteHandle() RemoteHandle
      +GetInstance(string queryPath) dynamic
      +GetInstances(string queryPath) IEnumerable~dynamic~
      +CreateInstance(string queryPath, params object[] args) dynamic
    }
    class DynamicRemoteObject {
      +foo
      -bar
      +baz()
      -quz()
    }
  }
  RemoteClient --> DynamicRemoteObject : GetInstance()
  RemoteClient --> DynamicRemoteObject : CreateInstance()
```

Here we use the RemoteClient to inject ClrMD into the MTGO process, allowing us to start the Snapshot Runtime to locate and interact with remote objects using reflection. The corresponding DynamicRemoteObject class is used to proxy interactions with remote objects, providing a way to interact with objects between processes.

## Starting MTGO

`RemoteClient.StartProcess` checks MTGO's deployment manifest before activating an installed ClickOnce deployment. An up-to-date deployment proceeds to startup without installation. If an update fails, startup can fall back to the installed deployment without another provider request.

Pass an `IProgress<LaunchProgress>` to display installation and startup progress:

```C#
using MTGOSDK.Core.Remoting;

var progress = new Progress<LaunchProgress>(update =>
{
  if (update.Stage == LaunchStage.Downloading && update.Percentage is int percentage)
    Console.WriteLine($"Downloading MTGO: {percentage}%");
  else
    Console.WriteLine(update.Stage);
});

await RemoteClient.StartProcess(progress);
```

Progress reports include a deployment version, download percentage and byte counts when available. An installation or update reports `UpdateCompleted` before `Started`; an up-to-date deployment proceeds to startup without that installation stage. Await `StartProcess` to handle failures that prevent MTGO from starting.

To receive progress while creating a `Client`, set `ClientOptions.LaunchProgress` and enable `CreateProcess`. For installation without starting MTGO, call `InstallOrUpdate(progress)`.

Construct `Progress<LaunchProgress>` on the UI thread when callbacks need to update controls there. Handle exceptions inside asynchronous callbacks, since they do not propagate through the startup task. Exceptions thrown directly by a custom `IProgress<LaunchProgress>.Report` implementation are logged without interrupting startup.

### Standalone Launcher

MTGOSDK includes a .NET Framework launcher that native hosts can run as a separate process:

```text
Launcher.exe --progress-json --start-and-wait
```

With `--progress-json`, stdout contains UTF-8 diagnostic lines and newline-delimited JSON progress records. Parse records by their numeric `stage` and allow optional fields to be absent; retain other lines as diagnostic output.

With `--start-and-wait`, the launcher reports `Started` after finding MTGO's application window and remains running until MTGO exits. A host can therefore track MTGO's lifetime by waiting for the launcher process. If startup fails, the process reports `Failed`, writes exception details to stderr and returns a nonzero exit code.

Hosts that already know the current MTGO version can pass `--expected-version <version>` after `--start-and-wait`. When the installed subscription is at that version or newer, activation uses the current ClickOnce bind without a provider request; otherwise startup follows the normal update path.

A deployment-manifest URI and an `.appref-ms` path may follow those options to override MTGO's defaults.

## Fetching Remote Objects

With the `GetInstance()` method, we can create a dynamic object that fetches a remote object in the client's heap memory. This method takes a string parameter that represents the fully qualified name of the object to be created, and returns a dynamic object that can be used to interact with the remote object.

We can do so using any of the following methods:

```C#
using MTGOSDK.Core.Reflection; // TypeProxy<T>
using MTGOSDK.Core.Remoting;   // RemoteClient

dynamic objB = RemoteClient.GetInstance(new TypeProxy<Bar>());
dynamic objC = RemoteClient.GetInstance(new TypeProxy<dynamic>(typeof(Bar)));
dynamic objA = RemoteClient.GetInstance("assembly.namespace.Bar");
```

> [!NOTE]
> Objects returned this way must be handled as dynamic objects, which defer type checking to runtime. Refer to the [`DLRWrapper`](./dlr-wrapper.md) class for a way to bind interfaces to dynamic objects in a type-safe manner.

Additionally, multiple instances of the object can be found by using `GetInstances()`, returning an enumerable collection of dynamic objects that represent the remote objects in the client's memory space.

```C#
using MTGOSDK.Core.Reflection.Proxy; // TypeProxy<T>
using MTGOSDK.Core.Remoting;         // RemoteClient

foreach (dynamic obj in RemoteClient.GetInstances("assembly.namespace.Bar"))
{
  // Do something with obj.
}
```

## Creating Remote Objects

Creating a new instance of a remote object is similarly straightforward, using the `CreateInstance()` method to invoke the object's constructor with the specified arguments. This method takes a string parameter that represents the fully qualified name of the object to be created, and an array of objects that represent the arguments to be passed to the constructor.

Below is an example of creating a new instance of a remote object with the `CreateInstance()` method, using the [`TypeProxy<T>`](/MTGOSDK/src/Core/Reflection/Proxy.cs) class to convert the interface type to a string containing the fully qualified name of the object. We'll assume that the `Bar` class has two constructors, one with two arguments and the other with three arguments.

```C#
class Bar : IDisposable
{
  public Bar(int A, string B) { }
  public Bar(int A, string B, string C) { }
  public void Dispose() { }
}
```

```C#
using MTGOSDK.Core.Reflection.Proxy; // TypeProxy<T>
using MTGOSDK.Core.Remoting;         // RemoteClient

// Invoke the first constructor with 2 arguments (A and B).
dynamic objA = RemoteClient.CreateInstance<Bar>(1, "foo");
// Invoke the second constructor with 3 arguments (A, B, and C).
dynamic objB = RemoteClient.CreateInstance<Bar>(2, "bar", "baz");

// Invoke the first constructor and dispose of the object when out of scope.
using (dynamic objC = RemoteClient.CreateInstance<Bar>(1, "quz"))
{
  // Do something with objC.
}
```

You can also use `RemoteClient.CreateInstance("Foo.Bar", 1, "foo")` to create a new instance of a remote object with the `CreateInstance()` method, using the fully qualified name of the object to be created.

## Remarks

As the `RemoteClient` class is a low-level class that interacts directly with the client's memory space, it is best to limit frequent use of the `RemoteClient` class to cases where the `MTGOSDK.API` classes are not sufficient. Each method in the `RemoteClient` class may traverse the client's memory heap or require reflection to retrieve or interact with objects, which can be slow and resource-intensive.

As such, it is recommended to use the `MTGOSDK.API` classes or the `ObjectProvider` class instead where possible, as these classes provide a safer and more optimized code-path for interacting with remote objects in the client's memory space.
