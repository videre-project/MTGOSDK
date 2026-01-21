# Event System

The SDK provides several event wrapper classes that enable subscribing to events on remote MTGO objects. These wrappers handle the complexity of cross-process event subscription, delegate proxying, and resource cleanup.

## Key Classes

<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.Core.Reflection.Proxy</code></td>
    <td><a href="/MTGOSDK/src/Core/Reflection/Proxy/EventProxy.cs"><code>EventProxy</code></a></td>
    <td>Subscribes to events on remote objects</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/Core/Reflection/Proxy/EventProxyBase.cs"><code>EventProxyBase</code></a></td>
    <td>Base class for event proxying with delegate conversion</td>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.Core.Reflection</code></td>
    <td><a href="/MTGOSDK/src/Core/Reflection/EventHookWrapper.cs"><code>EventHookWrapper</code></a></td>
    <td>Filtered event subscription with instance matching</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/Core/Reflection/EventWrapper.cs"><code>EventWrapper</code></a></td>
    <td>Simple wrapper for typed event handlers</td>
  </tr>
</table>

---

## EventProxy

`EventProxy` enables subscribing to events on remote MTGO objects using familiar `+=` and `-=` syntax. It handles marshaling callbacks across the process boundary and converting remote event args to SDK types.

### Basic Usage

```csharp
using MTGOSDK.API;

using var client = new Client();

// Subscribe to connection state changes (no args)
client.IsConnectedChanged += () =>
{
  Console.WriteLine($"Connection changed: {client.IsConnected}");
};

// Subscribe to errors (ErrorEventArgs only, no sender)
client.ErrorReceived += (ErrorEventArgs args) =>
{
  Console.WriteLine($"Error: {args.Exception.Message}");
};
```

### How It Works

`EventProxy` wraps a reference to a remote object and an event name. When you use `+=`, it:

1. Creates a proxy delegate that converts dynamic arguments to typed SDK objects
2. Registers the callback with ScubaDiver to receive remote event notifications
3. Stores a reference to the delegate for later cleanup

The `EventProxyBase.ProxyTypedDelegate` method handles the conversion, supporting handlers with 0, 1, or 2 parameters (sender + args).

### Clearing Subscriptions

```csharp
// Clear all handlers for a specific event
client.ErrorReceived.Clear();

// Or unsubscribe a specific handler
client.ErrorReceived -= myHandler;
```

---

## EventHookWrapper

`EventHookWrapper` provides filtered event subscription based on a hook condition. This is useful when you want to subscribe to a shared event source but only receive callbacks for a specific instance.

### Use Case: Match-Specific Events

Many MTGO events are broadcast globally rather than per-instance. For example, game state changes come from a central event source. `EventHookWrapper` lets you filter to events for a specific match or game:

```csharp
// In the Match class, OnGameStarted only fires for this match's games
public EventHookWrapper<Game> OnGameStarted =
  new(
    EventManager.GameStartedHook,          // Global event source
    (sender, game) => game.MatchId == Id   // Filter to this match
  );
```

### How It Works

The wrapper takes:
- A shared `EventHookProxy` that broadcasts events to all listeners
- A `Filter<T>` delegate that returns `true` to accept the event

When an event fires, the filter is checked first. Only matching events invoke your callback. This avoids the overhead of creating separate event subscriptions for each instance.

```csharp
// Subscribe like a normal event
match.OnGameStarted += (Game game) =>
{
  Console.WriteLine($"Game {game.Id} started in match {match.Id}");
};

// The filter ensures only games from this match trigger the callback
```

---

## EventWrapper

`EventWrapper<T>` is a simple utility for wrapping a standard `EventHandler` to work with typed event args:

```csharp
public class EventWrapper<T>(EventHandler handler) where T : EventArgs
{
  public void Handle(object sender, T args) => handler.Invoke(sender, args);
}
```

This is primarily used internally for bridging between SDK event types and standard .NET event patterns.

---

## Event Lifecycle

Events in the SDK follow a consistent lifecycle:

1. **Declaration** - Event proxies are declared as public properties on wrapper classes
2. **Subscription** - Use `+=` to add handlers; the proxy registers with ScubaDiver
3. **Invocation** - When MTGO raises the event, ScubaDiver forwards it to registered callbacks
4. **Cleanup** - Call `Clear()` or let the wrapper dispose to unregister all handlers

### Cleanup

Event subscriptions should be cleaned up when no longer needed to avoid memory leaks. Use the `Clear()` method on the event proxy:

```csharp
// Clear all handlers for a specific event
match.OnGameStarted.Clear();
match.OnGameEnded.Clear();

// Or clear events individually when unsubscribing
match.OnGameStarted -= myHandler;
```

For long-lived objects, call `Clear()` explicitly to remove handlers you no longer need.

---

## Best Practices

1. **Prefer instance-level events** - Use `EventHookWrapper` to filter global events to specific instances rather than checking in your callback.

2. **Clean up subscriptions** - Either dispose the wrapper or call `Clear()` when you're done. Leaked subscriptions can cause memory issues.

3. **Keep handlers lightweight** - Event callbacks run on the SDK's event dispatch thread. Heavy processing should be offloaded.

4. **Handle exceptions** - Exceptions in event handlers can disrupt other subscribers. Wrap handler bodies in try-catch.

```csharp
match.OnGameStarted += (game) =>
{
  try
  {
    ProcessGameStart(game);
  }
  catch (Exception ex)
  {
    Log.Error(ex, "Error processing game start");
  }
};
```
