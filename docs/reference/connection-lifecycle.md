# Connection Lifecycle Guide

This guide covers best practices for managing the MTGO client connection lifecycle, including crash recovery, reconnection patterns, and handling process restarts. These patterns are essential for building reliable, long-running applications.

> [!IMPORTANT]
> Long-running applications should implement robust connection management to handle MTGO crashes and restarts gracefully.

## Key Concepts

| Concept | Description |
| ------- | ----------- |
| `Client` | Main SDK entry point, manages connection |
| `RemoteClient` | Low-level connection to MTGO process |
| `ProcessCrashedException` | Thrown when MTGO process crashes mid-operation |
| `RemoteClient.Disposed` | Event fired when connection is lost |

```csharp
using MTGOSDK.API;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Remoting;
```

---

## Basic Connection Handling

For simple scripts that run once and exit, the `using` pattern handles cleanup automatically:

```csharp
using var client = new Client(new ClientOptions
{
  CreateProcess = true,
  CloseOnExit = true
});

await client.LogOn("username", password);
// Do work...
// Client is disposed when scope exits
```

The `using` declaration ensures that when your code exits the current scope (either normally or via exception), the `Client` is disposed properly. The `CreateProcess = true` option launches MTGO if it isn't running, and `CloseOnExit = true` terminates the MTGO process when the client is disposed.

---

## Detecting Disconnection

For applications that need to respond to MTGO closing or crashing, subscribe to the `IsConnectedChanged` event:

```csharp
using var client = new Client();

client.IsConnectedChanged += (sender) =>
{
  if (!client.IsConnected)
  {
    Console.WriteLine("MTGO disconnected!");
    // Trigger cleanup or reconnection logic
  }
};
```

This event fires whenever the connection state changes. The callback checks `IsConnected` to determine the new state. If `false`, MTGO has closed or crashed. Use this hook to trigger cleanup, notify users, or initiate reconnection.

---

## Reconnection Loop Pattern

For long-running services (like the Tracker), implement a reconnection loop that automatically recovers from crashes:

```csharp
public class ClientProvider
{
  public Client? Client { get; private set; }
  public bool IsReady { get; private set; } = false;
  
  public event EventHandler? ClientStateChanged;

  public async Task RunClientLoopAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      try
      {
        IsReady = false;
        ClientStateChanged?.Invoke(this, EventArgs.Empty);
        
        // Wait for MTGO process to start
        await Client.WaitForMTGOProcess(TimeSpan.MaxValue);
        
        // Initialize connection
        Client = new Client();
        
        // Wait for user login
        await Client.WaitForUserLogin(TimeSpan.MaxValue);
        
        // Wait for client to be fully ready
        await Client.WaitForClientReady();
        
        // Mark as ready
        IsReady = true;
        ClientStateChanged?.Invoke(this, EventArgs.Empty);
        
        // Wait until client is disposed (crash or close)
        var disposedTcs = new TaskCompletionSource<bool>();
        RemoteClient.Disposed += (s, e) => disposedTcs.TrySetResult(true);
        
        // Check if we missed the event
        if (RemoteClient.IsDisposed)
          disposedTcs.TrySetResult(true);
        
        await disposedTcs.Task;
        
        // Loop back to reconnect
      }
      catch (OperationCanceledException)
      {
        break; // Graceful shutdown
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error: {ex.Message}");
        await Task.Delay(1000, cancellationToken); // Avoid tight retry loop
      }
    }
  }
}
```

**How this works:**

1. **Outer while loop**: The loop runs continuously until cancellation is requested, automatically retrying after any disconnect.

2. **State notification**: Before initialization, we set `IsReady = false` and fire the event. This lets dependent services know that the client is unavailable.

3. **Sequential initialization**: We wait for three conditions in order:
   - `WaitForMTGOProcess` - Waits for MTGO.exe to start
   - `WaitForUserLogin` - Waits for the user to complete login
   - `WaitForClientReady` - Waits for MTGO to finish loading

4. **Ready notification**: Once fully initialized, we set `IsReady = true` so dependent services can begin operations.

5. **Await disposal**: We create a `TaskCompletionSource` and subscribe to `RemoteClient.Disposed`. When MTGO closes or crashes, this completes and we loop back to reconnect.

6. **Race condition protection**: Before awaiting, we check `RemoteClient.IsDisposed` in case the event fired between subscribing and awaiting.

7. **Error recovery**: If initialization fails, we wait 1 second before retrying to avoid spinning in a tight loop.

---

## Handling ProcessCrashedException

When MTGO crashes during an SDK operation, `ProcessCrashedException` is thrown. Your code should catch this and wait for reconnection:

```csharp
try
{
  var decks = CollectionManager.Decks.ToList();
}
catch (ProcessCrashedException ex)
{
  Console.WriteLine($"MTGO crashed: {ex.Message}");
  Console.WriteLine($"Process ID was: {ex.ProcessId}");
  
  // Wait for reconnection before retrying
  await clientProvider.WaitForClientReadyAsync();
  
  // Retry the operation
  var decks = CollectionManager.Decks.ToList();
}
```

The exception includes the `ProcessId` of the crashed process, which can be useful for logging. After catching the exception, wait for your `ClientProvider` (or equivalent) to signal that a new connection is ready before retrying the failed operation.

---

## Middleware Pattern (ASP.NET Core)

For web services, use middleware to transparently handle crashes and retry requests:

```csharp
public class ClientMiddleware(RequestDelegate next)
{
  public async Task InvokeAsync(HttpContext context)
  {
    try
    {
      await next(context);
    }
    catch (ProcessCrashedException)
    {
      if (context.Response.HasStarted)
        return;

      // Wait for reconnection
      var provider = context.RequestServices.GetRequiredService<IClientProvider>();
      await provider.WaitForClientReadyAsync(context.RequestAborted);

      if (!RemoteClient.IsInitialized)
      {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("MTGO client unavailable");
        return;
      }

      // Retry the request
      await next(context);
    }
  }
}
```

**Key implementation details:**

1. **Check `HasStarted`**: If the response has already begun streaming to the client, we can't modify it. We simply return and let the connection close.

2. **Wait for reconnection**: We retrieve the client provider from DI and wait for it to signal readiness, respecting the request's cancellation token.

3. **Verify initialization**: After waiting, we double-check `RemoteClient.IsInitialized`. If reconnection failed, we return a 503 Service Unavailable response.

4. **Transparent retry**: If reconnection succeeded, we call `next(context)` again to retry the entire request pipeline. The controller action runs again with a fresh connection.

---

## Semaphore Pattern for Initialization

Multiple threads may try to initialize the client simultaneously. Use a semaphore to ensure only one initialization runs at a time:

```csharp
private static readonly SemaphoreSlim _initSemaphore = new(1, 1);

public async Task EnsureInitializedAsync()
{
  await _initSemaphore.WaitAsync();
  try
  {
    if (Client != null)
    {
      // Cleanup previous client
      Client.Dispose();
      await RemoteClient.WaitForDisposeAsync()
        .WaitAsync(TimeSpan.FromSeconds(10));
    }
    
    await Client.WaitForMTGOProcess(TimeSpan.MaxValue);
    Client = new Client();
  }
  finally
  {
    _initSemaphore.Release();
  }
}
```

The `SemaphoreSlim(1, 1)` acts as a mutex, so only one thread can hold it at a time. The `try`/`finally` block ensures the semaphore is always released, even if an exception occurs.

The 10-second timeout on `WaitForDisposeAsync` prevents hanging if disposal takes too long. In production, you might log a warning if the timeout is reached and proceed anyway.

---

## Per-Request Cancellation

Cancel in-flight operations when the client disconnects to avoid wasted work and potential errors:

```csharp
public class ClientStateMonitor : IDisposable
{
  private readonly CancellationTokenSource _cts = new();
  
  public ClientStateMonitor(IClientProvider provider)
  {
    provider.ClientStateChanged += (s, e) =>
    {
      if (!provider.IsReady)
        _cts.Cancel();
    };
  }
  
  public CancellationToken Token => _cts.Token;
  
  public void Dispose() => _cts.Dispose();
}
```

This class wraps a `CancellationTokenSource` that cancels when the client disconnects. Inject it as a scoped service (per-request lifetime) and pass its `Token` to any async operations:

```csharp
// Usage in controller
public async Task<IActionResult> GetDecks(
  [FromServices] ClientStateMonitor monitor)
{
  var decks = await Task.Run(() => 
    CollectionManager.Decks.ToList(), 
    monitor.Token);
  
  return Ok(decks);
}
```

If MTGO disconnects while the request is running, the token cancels and the operation throws `OperationCanceledException`, which ASP.NET Core handles by closing the request cleanly.

---

## Key APIs

| API | Description |
| --- | ----------- |
| `Client.WaitForMTGOProcess()` | Wait for MTGO.exe to start |
| `Client.WaitForUserLogin()` | Wait for user to log in |
| `Client.WaitForClientReady()` | Wait for full initialization |
| `RemoteClient.IsInitialized` | Check if connection is active |
| `RemoteClient.IsDisposed` | Check if connection was closed |
| `RemoteClient.Disposed` | Event when connection closes |
| `RemoteClient.WaitForDisposeAsync()` | Wait for cleanup to complete |

---

## Best Practices

1. **Always handle `ProcessCrashedException`** in user-facing code. Don't let it bubble up as an unhandled exception.

2. **Use semaphores** to prevent concurrent initialization attempts, which can cause race conditions

3. **Implement retry loops** for long-running services that need to survive MTGO restarts

4. **Set timeouts** on cleanup operations to avoid hangs if MTGO becomes unresponsive

5. **Check `RemoteClient.IsDisposed`** before awaiting events, since the event may have already fired

6. **Log state changes** for debugging connection issues in production

7. **Use cancellation tokens** tied to client state to abort in-flight requests when MTGO disconnects

---

## Related Topics

- [Client Reference](../reference/client.md) - Client class API
- [Events Architecture](../architecture/events.md) - How EventProxy works
