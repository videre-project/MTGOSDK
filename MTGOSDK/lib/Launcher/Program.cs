/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Deployment.Application;
using System.Globalization;
using System.IO;
using System.Threading;

using MTGOSDK.Win32.Deployment;


namespace Launcher;

public static class Program
{
  private static readonly object OutputLock = new();
  private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(5);

  public static int Main(string[] args)
  {
    try
    {
      if (args.Length == 2 &&
          string.Equals(args[0], "--activate-cached", StringComparison.Ordinal))
      {
        WriteMessage($"Activating the installed MTGO deployment from {args[1]}.");
        CachedDeploymentActivator.Activate(args[1]);
        WriteMessage("Installed MTGO deployment activated.");
        return 0;
      }

      if (args.Length != 1)
        throw new ArgumentException(
            "Usage: Launcher.exe <deployment-manifest-uri> " +
            "or Launcher.exe --activate-cached <appref-ms-path>");

      var manifestUri = new Uri(args[0], UriKind.Absolute);
      WriteMessage($"Starting MTGO update check for {manifestUri.AbsoluteUri}.");

      ManifestRequestBuilder.Register(manifestUri.AbsoluteUri);

      using var hostingManager =
        new InPlaceHostingManager(
          manifestUri,
          launchInHostProcess: false);
      using var completed = new ManualResetEventSlim();
      var progressReporter = new ProgressReporter();

      var failureLock = new object();
      Exception? failure = null;

      void SetFailure(Exception? error)
      {
        if (error is null)
          return;

        lock (failureLock)
          failure ??= error;
      }

      //
      // Keep the installation event handlers inside the ClickOnce callbacks.
      //
      // IPHM has internal locking and partial thread affinity, and returning
      // to Main between operations can race the deployment transaction under Wine.
      //
      hostingManager.DownloadProgressChanged += (sender, e) =>
      {
        progressReporter.Report(e);
      };

      hostingManager.DownloadApplicationCompleted += (sender, e) =>
      {
        if (e.Error is null && !e.Cancelled)
          WriteMessage("MTGO update completed.");

        SetFailure(GetFailure(
            e.Error,
            e.Cancelled,
            e.LogFilePath,
            "download the application"));
        completed.Set();
      };

      hostingManager.GetManifestCompleted += (sender, e) =>
      {
        var manifestFailure = GetFailure(
            e.Error,
            e.Cancelled,
            e.LogFilePath,
            "download the deployment manifest");
        SetFailure(manifestFailure);

        if (manifestFailure is not null)
        {
          completed.Set();
          return;
        }

        WriteMessage(
            $"MTGO update manifest ready: version {e.Version}, " +
            $"product {e.ProductName}.");

        try
        {
          // Verify and grant permissions specified in the application manifest.
          WriteMessage("Verifying MTGO application requirements.");
          hostingManager.AssertApplicationRequirements(true);

          // Download and install the application.
          WriteMessage("Downloading the MTGO application.");
          hostingManager.DownloadApplicationAsync();
        }
        catch (Exception ex)
        {
          SetFailure(ex);
          completed.Set();
        }
      };

      hostingManager.GetManifestAsync();

      if (!completed.Wait(OperationTimeout))
        throw new TimeoutException(
            $"Timed out after {OperationTimeout.TotalMinutes:0} minutes " +
            "waiting for the ClickOnce installation to complete.");

      Exception? finalFailure;
      lock (failureLock)
        finalFailure = failure;
      ThrowIfFailed(finalFailure);

      return 0;
    }
    catch (Exception ex)
    {
      WriteMessage("MTGO update failed.");
      // Pipe the exception to STDERR.
      Console.Error.WriteLine(ex);
      return 1;
    }
  }

  private static void WriteMessage(string message) =>
    WriteLine(message);

  private static void WriteLine(string line)
  {
    lock (OutputLock)
    {
      try
      {
        Console.WriteLine(line);
      }
      catch (IOException)
      {
        // Progress reporting must not interrupt the ClickOnce transaction.
      }
      catch (ObjectDisposedException)
      {
        // The parent may close stdout while shutting down.
      }
    }
  }

  // Console progress events can arrive rapidly and out of order.
  // So instead we preserve state/percentage changes while limiting
  // byte-only updates to 4 Hz.
  private sealed class ProgressReporter
  {
    private static readonly TimeSpan MinimumInterval =
      TimeSpan.FromMilliseconds(250);

    private readonly object _lock = new();
    private DeploymentProgressState? _lastState;
    private int _lastPercentage = -1;
    private long _lastBytesDownloaded = -1;
    private long _lastTotalBytes = -1;
    private DateTime _lastEmittedAt = DateTime.MinValue;

    public void Report(DownloadProgressChangedEventArgs progress)
    {
      DateTime now = DateTime.UtcNow;
      lock (_lock)
      {
        bool stateChanged = _lastState != progress.State;
        bool percentageChanged =
          _lastPercentage != progress.ProgressPercentage;
        bool bytesChanged =
          _lastBytesDownloaded != progress.BytesDownloaded;
        bool totalBytesChanged =
          _lastTotalBytes != progress.TotalBytesToDownload;

        bool meaningfulChange =
          stateChanged ||
          percentageChanged ||
          bytesChanged ||
          totalBytesChanged;

        if (!meaningfulChange)
          return;

        if (!stateChanged &&
            !percentageChanged &&
            (now - _lastEmittedAt) < MinimumInterval)
          return;

        _lastState = progress.State;
        _lastPercentage = progress.ProgressPercentage;
        _lastBytesDownloaded = progress.BytesDownloaded;
        _lastTotalBytes = progress.TotalBytesToDownload;
        _lastEmittedAt = now;

        WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "MTGO download progress: {0}; {1}% ({2} of {3} bytes).",
            progress.State,
            progress.ProgressPercentage,
            progress.BytesDownloaded,
            progress.TotalBytesToDownload));
      }
    }
  }

  private static Exception? GetFailure(
      Exception? error,
      bool cancelled,
      string? logFilePath,
      string operation)
  {
    if (error is not null)
    {
      string log = string.IsNullOrWhiteSpace(logFilePath)
        ? string.Empty
        : $" ClickOnce log: {logFilePath}";
      return new InvalidOperationException(
          $"Could not {operation}. Error: {error.Message}{log}", error);
    }

    return cancelled
      ? new OperationCanceledException(
          $"The operation to {operation} was cancelled.")
      : null;
  }

  private static void ThrowIfFailed(Exception? failure)
  {
    if (failure is not null)
      throw failure;
  }
}
