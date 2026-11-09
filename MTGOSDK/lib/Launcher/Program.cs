/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Deployment.Application;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.Core.Remoting;
using MTGOSDK.Win32.Deployment;


namespace Launcher;

public static class Program
{
  private static readonly object s_outputLock = new();
  private static bool s_progressJson;
  private static readonly TimeSpan s_operationTimeout = TimeSpan.FromMinutes(5);

  public static int Main(string[] args)
  {
    bool standalone = false;
    try
    {
      if (args.Length > 0 && args[0] == "--progress-json")
      {
        s_progressJson = true;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
          AutoFlush = true
        });
        var remaining = new string[args.Length - 1];
        Array.Copy(args, 1, remaining, 0, remaining.Length);
        args = remaining;
      }

      if (args.Length >= 1 &&
          string.Equals(args[0], "--start-and-wait", StringComparison.Ordinal))
      {
        standalone = true;
        int next = 1;
        Version? expectedVersion = null;
        if (args.Length > next && args[next] == "--expected-version")
        {
          if (args.Length <= next + 1 || !Version.TryParse(args[next + 1], out expectedVersion))
            throw new ArgumentException("--expected-version requires an MTGO version.");
          next += 2;
        }
        if (args.Length - next > 2)
          throw new ArgumentException("Too many arguments for --start-and-wait.");
        var manifestUri = new Uri(
            args.Length > next ? args[next] : DeploymentDefaults.ApplicationUri,
            UriKind.Absolute);
        string appRefPath = args.Length > next + 1 ? args[next + 1] : DeploymentDefaults.AppRefPath;
        StartAndWait(manifestUri, appRefPath, expectedVersion);
        return 0;
      }

      if (args.Length == 2 &&
          string.Equals(args[0], "--activate-cached", StringComparison.Ordinal))
      {
        ActivateCached(args[1]);
        return 0;
      }

      if (args.Length != 1)
        throw new ArgumentException(
            "Usage: Launcher.exe [--progress-json] <deployment-manifest-uri> " +
            "or --activate-cached <appref-ms-path> " +
            "or --start-and-wait [--expected-version <version>] [<deployment-manifest-uri> [<appref-ms-path>]]");

      InstallOrUpdate(new Uri(args[0], UriKind.Absolute));

      return 0;
    }
    catch (Exception ex)
    {
      WriteLine(standalone ? "MTGO startup failed." : "MTGO update failed.");
      if (standalone)
        WriteProgress(new LaunchProgress(LaunchStage.Failed, message: ex.Message));
      // Pipe the exception to STDERR.
      Console.Error.WriteLine(ex);
      return 1;
    }
  }

  private static void StartAndWait(Uri manifestUri, string appRefPath, Version? expectedVersion)
  {
    Version? installedVersion = null;
    if (expectedVersion is not null)
    {
      try
      {
        installedVersion = CachedDeploymentActivator.GetCurrentVersion(appRefPath);
      }
      catch (Exception exception)
      {
        // An unreadable subscription must go through the normal installation path.
        WriteLine($"Could not read the installed MTGO version. {exception.Message}");
        Console.Error.WriteLine(exception);
      }
    }

    if (expectedVersion is not null && installedVersion is not null && installedVersion >= expectedVersion)
    {
      WriteLine($"MTGO {installedVersion} is already installed; skipping the deployment-provider check.");
      WriteProgress(new LaunchProgress(LaunchStage.CheckingForUpdates, version: installedVersion.ToString()));
    }
    else
    {
      try
      {
        InstallOrUpdate(manifestUri);
      }
      catch (Exception exception) when (File.Exists(appRefPath))
      {
        WriteLine($"MTGO update failed; starting the installed deployment. {exception.Message}");
        Console.Error.WriteLine(exception);
      }
    }

    WriteProgress(new LaunchProgress(LaunchStage.Starting));
    Task activation = Task.Run(() => ActivateCached(appRefPath));
    if (Task.WhenAny(activation, Task.Delay(TimeSpan.FromSeconds(30)))
        .GetAwaiter().GetResult() != activation)
      throw new TimeoutException("The installed MTGO deployment did not activate.");
    activation.GetAwaiter().GetResult();
    WaitForGameExit();
  }

  private static void WaitForGameExit()
  {
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(60))
    {
      foreach (Process process in Process.GetProcessesByName("MTGO"))
      {
        using (process)
        {
          if (process.MainWindowTitle == "Magic: The Gathering Online")
          {
            WriteProgress(new LaunchProgress(LaunchStage.Started));
            process.WaitForExit();
            return;
          }
        }
      }
      Thread.Sleep(100);
    }
    throw new TimeoutException("MTGO did not open its main window.");
  }

  private static void ActivateCached(string appRefPath)
  {
    WriteLine($"Activating the installed MTGO deployment from {appRefPath}.");
    CachedDeploymentActivator.Activate(appRefPath);
    WriteLine("Installed MTGO deployment activated.");
  }

  private static void InstallOrUpdate(Uri manifestUri)
  {
    WriteLine($"Starting MTGO update check for {manifestUri.AbsoluteUri}.");
    WriteProgress(new LaunchProgress(LaunchStage.CheckingForUpdates));

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
      {
        WriteLine("MTGO update completed.");
        WriteProgress(new LaunchProgress(LaunchStage.UpdateCompleted));
      }

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

      WriteLine(
          $"MTGO update manifest ready: version {e.Version}, " +
          $"product {e.ProductName}.");

      try
      {
        // Cached applications can include rollback versions. Only skip
        // installation when the manifest matches the current subscription bind.
        if (e.IsCached && CachedDeploymentActivator.IsCurrent(
              e.SubscriptionIdentity, e.ApplicationIdentity.FullName))
        {
          WriteLine($"MTGO {e.Version} is already installed; skipping installation.");
          WriteProgress(new LaunchProgress(
              LaunchStage.CheckingForUpdates, version: e.Version.ToString()));
          completed.Set();
          return;
        }

        // Verify and grant permissions specified in the application manifest.
        WriteLine("Verifying MTGO application requirements.");
        WriteProgress(new LaunchProgress(
            LaunchStage.VerifyingRequirements, version: e.Version.ToString()));
        hostingManager.AssertApplicationRequirements(true);

        // Download and install the application.
        WriteLine("Downloading the MTGO application.");
        hostingManager.DownloadApplicationAsync();
      }
      catch (Exception ex)
      {
        SetFailure(ex);
        completed.Set();
      }
    };

    hostingManager.GetManifestAsync();

    if (!completed.Wait(s_operationTimeout))
      throw new TimeoutException(
          $"Timed out after {s_operationTimeout.TotalMinutes:0} minutes " +
          "waiting for the ClickOnce installation to complete.");

    Exception? finalFailure;
    lock (failureLock)
      finalFailure = failure;
    ThrowIfFailed(finalFailure);
  }

  private static void WriteLine(string line)
  {
    lock (s_outputLock)
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

  private static void WriteProgress(LaunchProgress progress)
  {
    if (s_progressJson) WriteLine(LaunchProgressProtocol.Serialize(progress));
  }

  // Emit state and percentage changes immediately; throttle byte-only updates
  // using elapsed time so clock adjustments do not affect reporting.
  private sealed class ProgressReporter
  {
    private static readonly TimeSpan s_minimumInterval =
      TimeSpan.FromMilliseconds(250);

    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DeploymentProgressState? _lastState;
    private int _lastPercentage = -1;
    private long _lastBytesDownloaded = -1;
    private long _lastTotalBytes = -1;
    private TimeSpan _lastEmittedAt;

    public void Report(DownloadProgressChangedEventArgs progress)
    {
      lock (_lock)
      {
        TimeSpan now = _clock.Elapsed;
        bool stateChanged = _lastState != progress.State;
        bool percentageChanged =
          _lastPercentage != progress.ProgressPercentage;
        if (!stateChanged && !percentageChanged)
        {
          if (_lastBytesDownloaded == progress.BytesDownloaded &&
              _lastTotalBytes == progress.TotalBytesToDownload)
            return;
          if (now - _lastEmittedAt < s_minimumInterval)
            return;
        }

        _lastState = progress.State;
        _lastPercentage = progress.ProgressPercentage;
        _lastBytesDownloaded = progress.BytesDownloaded;
        _lastTotalBytes = progress.TotalBytesToDownload;
        _lastEmittedAt = now;

        WriteProgress(new LaunchProgress(
            progress.State == DeploymentProgressState.DownloadingApplicationFiles
              ? LaunchStage.Downloading : LaunchStage.CheckingForUpdates,
            progress.ProgressPercentage,
            progress.BytesDownloaded,
            progress.TotalBytesToDownload));
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
