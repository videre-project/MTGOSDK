# Logging

The SDK provides a logging system built on `Microsoft.Extensions.Logging` that integrates with any standard .NET logging provider. The system automatically detects caller context, supports structured logging, and can be temporarily suppressed for noisy operations.

## Key Classes

<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="3"><code>MTGOSDK.Core.Logging</code></td>
    <td><a href="/MTGOSDK/src/Core/Logging/Log.cs"><code>Log</code></a></td>
    <td>Static facade for logging from anywhere in the SDK</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/Core/Logging/LoggerBase.cs"><code>LoggerBase</code></a></td>
    <td>Base class with automatic caller type detection</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/Core/Logging/FileLogger.cs"><code>FileLogger</code></a></td>
    <td>File-based logging provider</td>
  </tr>
</table>

---

## Using the Log Class

The `Log` class provides static methods for logging at different levels. It automatically determines the calling class and uses that as the logger category:

```csharp
using MTGOSDK.Core.Logging;

Log.Trace("Detailed diagnostic info");
Log.Debug("Debugging information");
Log.Information("Normal operation: {Event}", eventName);
Log.Warning("Something unexpected: {Message}", message);
Log.Error(exception, "Operation failed");
Log.Critical("Fatal error, shutting down");
```

The logging system uses structured logging, so you can include named parameters that will be parsed by logging providers that support structured output. This will automatically trace the calling class and method invoked.

---

## Configuring Logging

Logging is configured using the static methods on `LoggerBase`. You can provide either an `ILoggerFactory` or an `ILoggerProvider`:

```csharp
using Microsoft.Extensions.Logging;
using MTGOSDK.Core.Logging;

// Using ILoggerFactory (recommended)
ILoggerFactory factory = LoggerFactory.Create(builder =>
{
  builder.AddConsole();
  builder.SetMinimumLevel(LogLevel.Debug);
});

LoggerBase.SetFactoryInstance(factory);
```

For custom providers (like Serilog or NLog):

```csharp
using Serilog;
using Serilog.Extensions.Logging;
using MTGOSDK.Core.Logging;

var serilogLogger = new LoggerConfiguration()
  .MinimumLevel.Debug()
  .WriteTo.File("logs/mtgosdk.log")
  .CreateLogger();

LoggerBase.SetProviderInstance(new SerilogLoggerProvider(serilogLogger));
```

When you set a new factory or provider, any cached logger instances are cleared so subsequent log calls use the new configuration.

---

## Automatic Caller Detection

The `LoggerBase` class automatically walks the call stack to determine which class is making the log call. This means you don't need to create separate logger instances for each class. The detected type becomes the logger category.

For compiler-generated types (lambdas, async state machines), the system resolves back to the containing class so log entries show meaningful categories rather than internal compiler names.

---

## Suppressing Logs

For operations that generate noisy logs (like batch operations), you can temporarily suppress logging:

```csharp
using MTGOSDK.Core.Logging;

// Suppress all logs in this scope
using (Log.Suppress())
{
  // These operations won't log anything
  foreach (var item in collection)
  {
    ProcessItem(item);
  }
}

// Suppress only below a certain level
using (Log.Suppress(LogLevel.Warning))
{
  // Only Warning, Error, Critical will log
  ProcessNoisyOperation();
}
```

The suppression is scope-based and thread-safe, so it only affects the current execution context.

---

## File Logging

The SDK includes a simple file logger for cases where you want logging without additional dependencies:

```csharp
using MTGOSDK.Core.Logging;

var fileProvider = new FileLoggerProvider(new FileLoggerOptions
{
  LogDirectory = "logs",
  LogFileName = "mtgosdk.log",
  MinimumLevel = LogLevel.Information
});

LoggerBase.SetProviderInstance(fileProvider);
```

For production use, consider a full-featured logging framework like Serilog or NLog instead.
