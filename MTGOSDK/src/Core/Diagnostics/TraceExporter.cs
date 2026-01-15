/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core.Diagnostics;

/// <summary>
/// Exports System.Diagnostics.Activity traces to a Chromium Trace Event format JSON file.
/// Viewable in chrome://tracing, edge://tracing, or https://ui.perfetto.dev
/// </summary>
public class TraceExporter : IDisposable
{
  private readonly string _outputPath;
  private readonly string _processName;
  private readonly int _processId;
  private readonly ActivityListener _listener;
  private readonly ConcurrentQueue<Activity> _finishedActivities = new();
  private readonly CancellationTokenSource _cts = new();
  private readonly Thread _writeThread;
  private readonly object _fileLock = new();

  public TraceExporter(string outputPath, string processName)
  {
    _outputPath = outputPath;
    _processName = processName;
    _processId = Process.GetCurrentProcess().Id;

    // Ensure directory exists
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

    // NOTE: We no longer delete the existing file - traces accumulate across sessions
    // This allows long-running apps like Tracker to build up traces over time

    _listener = new ActivityListener
    {
      ShouldListenTo = source => source.Name == "MTGOSDK.Core" || source.Name == "ScubaDiver",
      Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
      ActivityStopped = OnActivityStopped
    };
    ActivitySource.AddActivityListener(_listener);

    // Background thread to flush periodically
    _writeThread = new Thread(WriteLoop)
    {
      IsBackground = true,
      Name = $"TraceExporter-{processName}"
    };
    _writeThread.Start();
  }

  private void OnActivityStopped(Activity activity)
  {
    _finishedActivities.Enqueue(activity);
  }

  private void WriteLoop()
  {
    try
    {
      while (!_cts.IsCancellationRequested)
      {
        Thread.Sleep(5000); // Flush every 5 seconds
        Flush();
      }
    }
    catch (Exception ex)
    {
      Log.Error($"[TraceExporter] Write loop error: {ex.Message}");
    }
  }

  public void Dispose()
  {
    Log.Debug($"[TraceExporter] Disposing, pending activities: {_finishedActivities.Count}");
    _cts.Cancel();
    _writeThread.Join(1000); // Wait up to 1s for thread to exit
    _listener.Dispose();
    Flush(); // Final flush on exit
    Log.Debug($"[TraceExporter] Disposed, file exists: {File.Exists(_outputPath)}");
  }

  private void Flush()
  {
    if (_finishedActivities.IsEmpty) return;

    var activities = new List<Activity>();
    while (_finishedActivities.TryDequeue(out var activity))
    {
      activities.Add(activity);
    }

    if (activities.Count == 0) return;

    // Sort by start time for cleaner viewing
    activities.Sort((a, b) => a.StartTimeUtc.CompareTo(b.StartTimeUtc));

    var events = new List<TraceEvent>();

    foreach (var act in activities)
    {
      // Convert valid tags to arguments
      var args = new Dictionary<string, object>();
      string flowType = null;
      int threadId = 0; // Default to 0 if not specified
      foreach (var tag in act.Tags)
      {
        if (tag.Key == "ipc.flow")
        {
          flowType = tag.Value;
        }
        else if (tag.Key == "thread.id")
        {
          int.TryParse(tag.Value, out threadId);
        }
        else
        {
          args[tag.Key] = tag.Value;
        }
      }

      // Use thread ID for visualization lanes (allows proper nesting within threads)
      string flowId = act.SpanId.ToHexString(); // Unique ID for flow linking

      // Complete Event (X)
      events.Add(new TraceEvent
      {
        Name = act.DisplayName,
        Category = "api",
        Phase = "X",
        Timestamp = act.StartTimeUtc.Ticks / 10, // Microseconds
        Duration = act.Duration.Ticks / 10,
        ProcessId = _processId,
        ThreadId = threadId, 
        Args = args.Count > 0 ? args : null
      });

      // Flow Events for IPC visualization
      if (flowType == "start")
      {
        // Flow Start: emitted at the END of the activity (when request is sent)
        events.Add(new TraceEvent
        {
          Name = act.DisplayName,
          Category = "ipc",
          Phase = "s", // Flow Start
          Timestamp = (act.StartTimeUtc + act.Duration).Ticks / 10, // End of activity
          ProcessId = _processId,
          ThreadId = threadId,
          FlowId = flowId,
          FlowBindingPoint = "e" // Enclosing slice end
        });
      }
      else if (flowType == "end")
      {
        // Flow End: emitted at the START of the activity (when server begins processing)
        events.Add(new TraceEvent
        {
          Name = act.DisplayName,
          Category = "ipc",
          Phase = "f", // Flow Finish
          Timestamp = act.StartTimeUtc.Ticks / 10, // Start of activity
          ProcessId = _processId,
          ThreadId = threadId,
          FlowId = flowId,
          FlowBindingPoint = "e" // Enclosing slice start
        });
      }
    }

    lock (_fileLock)
    {
      try
      {
        // Read existing events from file if it exists
        var existingEvents = new List<TraceEvent>();
        if (File.Exists(_outputPath))
        {
          try
          {
            var existingJson = File.ReadAllText(_outputPath);
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
              using var doc = JsonDocument.Parse(existingJson);
              if (doc.RootElement.TryGetProperty("traceEvents", out var traceEventsElement))
              {
                foreach (var evt in traceEventsElement.EnumerateArray())
                {
                  // Reconstruct TraceEvent from JSON
                  var traceEvent = JsonSerializer.Deserialize<TraceEvent>(evt.GetRawText(), new JsonSerializerOptions
                  {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                  });
                  if (traceEvent != null)
                  {
                    existingEvents.Add(traceEvent);
                  }
                }
              }
            }
          }
          catch (Exception ex)
          {
            Log.Debug($"[TraceExporter] Could not read existing events: {ex.Message}");
          }
        }

        // Merge existing events with new events
        existingEvents.AddRange(events);

        // Wrap in object with traceEvents array for official format support
        var traceFile = new TraceFile 
        { 
          TraceEvents = existingEvents,
          DisplayTimeUnit = "ms"
        };

        var options = new JsonSerializerOptions 
        { 
          WriteIndented = false,
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
#pragma warning disable SYSLIB0020 // IgnoreNullValues is obsolete
          IgnoreNullValues = true
#pragma warning restore SYSLIB0020
        };
        
        var json = JsonSerializer.Serialize(traceFile, options);
        File.WriteAllText(_outputPath, json);
      }
      catch (Exception ex)
      {
        Log.Error($"[TraceExporter] Write file error: {ex.Message}");
      }
    }
  }

  // Chromium Trace Event Format
  // https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/edit

  private class TraceFile
  {
    [JsonPropertyName("traceEvents")]
    public List<TraceEvent> TraceEvents { get; set; }

    [JsonPropertyName("displayTimeUnit")]
    public string DisplayTimeUnit { get; set; } = "ms";
  }

  private class TraceEvent
  {
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("cat")]
    public string Category { get; set; }

    [JsonPropertyName("ph")]
    public string Phase { get; set; }

    [JsonPropertyName("ts")]
    public long Timestamp { get; set; }

    [JsonPropertyName("dur")]
    public long Duration { get; set; }

    [JsonPropertyName("pid")]
    public int ProcessId { get; set; }

    [JsonPropertyName("tid")]
    public object ThreadId { get; set; }

    [JsonPropertyName("args")]
    public Dictionary<string, object> Args { get; set; }

    [JsonPropertyName("id")]
    public string FlowId { get; set; }

    [JsonPropertyName("bp")]
    public string FlowBindingPoint { get; set; }
  }
}
