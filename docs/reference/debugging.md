# Debugging

This document explains how to diagnose issues with the SDK using log files and performance traces. The SDK generates diagnostic artifacts in `%LocalAppData%\MTGOSDK` that help troubleshoot connection problems, performance issues, and unexpected behavior.

## Log and Trace Locations

All SDK diagnostic files are stored under:

```
%LocalAppData%\MTGOSDK\
├── Logs\
│   ├── Diver-{port}.log        # ScubaDiver log (one per session)
│   └── trace\
│       ├── trace_sdk.json      # SDK-side trace events
│       ├── trace_diver.json    # ScubaDiver-side trace events
│       └── trace_combined.json # Merged trace (created on dispose)
│
├── bin\                        # Extracted runtime binaries
└── Launcher.exe                # ClickOnce installer
```

---

## ScubaDiver Logs

ScubaDiver is the injected component that runs inside the MTGO process. It writes detailed logs about IPC requests, object access, and any errors that occur within MTGO's memory space.

### Log File Location

```
%LocalAppData%\MTGOSDK\Logs\Diver-{port}.log
```

The `{port}` is typically the MTGO process ID, which the SDK uses as the IPC port number. For example, if MTGO is running as PID 12345, the log file would be `Diver-12345.log`.

### Log Contents

The Diver log captures:
- IPC requests received from the SDK
- Object lookups and property access
- Method invocations on remote objects
- Event subscriptions and callbacks
- Errors and exceptions within MTGO's process

### Example Log Entries

```
[2026-01-21 10:15:32.456] [Debug] [DiverHost] Diver starting on port 12345
[2026-01-21 10:15:32.501] [Debug] [TcpServer] Listening on 127.0.0.1:12345
[2026-01-21 10:15:33.123] [Debug] [RequestHandler] GetInstances: WotC.MtGO.Client.*
[2026-01-21 10:15:33.456] [Debug] [RequestHandler] Found 3 instances
[2026-01-21 10:15:34.789] [Error] [RequestHandler] Failed to get property: Object not found in pinned pool
```

### Log Rotation

Logs are automatically cleaned up after 3 days:

```csharp
FileLoggerOptions options = new()
{
  LogDirectory = Path.Combine(Bootstrapper.AppDataDir, "Logs"),
  FileName = $"Diver-{port}.log",
  MaxAge = TimeSpan.FromDays(3),
};
```

Old log files are deleted on startup to prevent disk space accumulation.

---

## Performance Tracing

The SDK includes a tracing infrastructure that records timing information for SDK operations and IPC calls. Traces are written in Chrome Trace Event format, viewable in browser-based trace viewers.

### Trace Files

| File | Contents |
|------|----------|
| `trace_sdk.json` | Traces from SDK-side operations (property access, method calls) |
| `trace_diver.json` | Traces from ScubaDiver (IPC handling, object lookups) |
| `trace_combined.json` | Both merged for end-to-end visibility |

### Viewing Traces

Open any of these files in:
- `chrome://tracing` (Chrome)
- `edge://tracing` (Edge)
- [Perfetto UI](https://ui.perfetto.dev) (any browser)

The trace viewer shows a timeline of all SDK operations, with nesting that shows how time is spent.

### What's Traced

The SDK uses `System.Diagnostics.ActivitySource` to record spans:

```csharp
private static readonly ActivitySource s_activitySource = new("MTGOSDK.Core");

public static T As(object obj)
{
  using var activity = s_activitySource.StartActivity("TypeProxy.As");
  activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
  activity?.SetTag("interface", typeof(T).Name);
  
  // ... operation code ...
}
```

Traced operations include:
- `RemoteClient.StartProcess` - MTGO launch time
- `RemoteClient.GetClientHandle` - Connection establishment
- `TypeProxy.As` - Proxy creation
- IPC requests (with `ipc.flow` tags for cross-process linking)

### How TraceExporter Works

The `TraceExporter` class listens to `ActivitySource` events and writes them to JSON files:

```csharp
_listener = new ActivityListener
{
  ShouldListenTo = source => 
    source.Name == "MTGOSDK.Core" || source.Name == "ScubaDiver",
  Sample = (ref ActivityCreationOptions<ActivityContext> _) => 
    ActivitySamplingResult.AllData,
  ActivityStopped = OnActivityStopped
};
ActivitySource.AddActivityListener(_listener);
```

Traces accumulate across sessions and are flushed every 5 seconds. On `RemoteClient.Dispose()`, the SDK merges SDK and Diver traces into `trace_combined.json`.

### IPC Flow Visualization

Traces include flow events that link SDK requests to Diver responses:

```csharp
activity?.SetTag("ipc.flow", "start");  // SDK side
activity?.SetTag("ipc.flow", "end");    // Diver side
```

In the trace viewer, these appear as arrows connecting the SDK call to the corresponding Diver processing, making it easy to see IPC round-trip times.

---

## Enabling Verbose Logging

By default, the SDK logs at `Debug` level to the Diver file. To capture more detail or route logs elsewhere, configure logging before creating a `Client`:

```csharp
using Microsoft.Extensions.Logging;
using MTGOSDK.Core.Logging;

// Console logging with Trace level
ILoggerFactory factory = LoggerFactory.Create(builder =>
{
  builder.AddConsole();
  builder.SetMinimumLevel(LogLevel.Trace);
});
LoggerBase.SetFactoryInstance(factory);

// Now create Client - logs will go to console
var client = new Client();
```

### Log Levels

| Level | Contents |
|-------|----------|
| Trace | Very detailed diagnostic info (property access, every IPC call) |
| Debug | Diagnostic info (connection status, major operations) |
| Information | Normal operational events |
| Warning | Unexpected but recoverable situations |
| Error | Failures that affect functionality |
| Critical | Fatal errors |

### Suppressing Noisy Logs

For batch operations that generate many log entries:

```csharp
using (Log.Suppress())
{
  // Process thousands of items without logging each one
  foreach (var card in collection)
  {
    Process(card);
  }
}
```

---

## Common Debugging Scenarios

### Connection Failures

**Symptom:** SDK throws `TimeoutException` or `NullReferenceException`

**Check:**
1. Diver log exists at `%LocalAppData%\MTGOSDK\Logs\Diver-{pid}.log`
2. Look for `TcpServer Listening` - confirms Diver started
3. Look for errors after that point

**Common causes:**
- MTGO crashed during injection
- Antivirus blocked the injection
- Port conflict with another application

### "Object not found in pinned pool"

**Symptom:** `ArgumentException` when accessing properties

**Check:**
1. Look for `[GCTimer]` entries in Diver log
2. Check if object was unpinned prematurely

**Common cause:** Reference went out of scope and was garbage collected before access

### Slow Performance

**Symptom:** Operations taking longer than expected

**Check:**
1. Open `trace_combined.json` in a trace viewer
2. Look for long bars - these indicate slow operations
3. Check IPC flow arrows - long gaps indicate slow serialization

**Common causes:**
- Accessing properties one-by-one instead of using `SerializeItemsAs<T>`
- Event handlers doing too much work
- GC pressure in MTGO process

### Missing Trace Data

**Symptom:** Trace files are empty or missing

**Check:**
1. Ensure `RemoteClient.Dispose()` was called (flushes traces)
2. Check for TraceExporter errors in SDK logs
3. Verify write permissions to `%LocalAppData%\MTGOSDK\Logs\trace`

---

## Programmatic Access

### Reading Diver Logs

```csharp
string logDir = Path.Combine(
  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
  "MTGOSDK", "Logs");

var logFiles = Directory.GetFiles(logDir, "Diver-*.log");
foreach (var file in logFiles)
{
  Console.WriteLine($"Log: {file}");
  Console.WriteLine(File.ReadAllText(file));
}
```

### Checking Trace File Location

```csharp
string traceDir = Path.Combine(
  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
  "MTGOSDK", "Logs", "trace");

if (Directory.Exists(traceDir))
{
  foreach (var file in Directory.GetFiles(traceDir, "*.json"))
  {
    Console.WriteLine($"Trace: {file} ({new FileInfo(file).Length} bytes)");
  }
}
```

---

## See Also

- [Logging](../architecture/logging.md) - Logging configuration details
- [Remote Client](../architecture/remote-client.md) - Connection internals
- [Memory](../architecture/memory.md) - GC coordination and pinned objects
