/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Resources;


namespace MTGOSDK.UnitTests;

/// <summary>
/// SDK launcher calls must deliver final output, propagate exit failures and
/// terminate on timeout. A shell replaces only the embedded executable; no MTGO
/// installation or injection is performed.
/// </summary>
[NonParallelizable]
public class LauncherProcess
{
  private static readonly FieldInfo s_runtime = typeof(Bootstrapper)
    .GetField("s_runtime", BindingFlags.NonPublic | BindingFlags.Static)!;
  private static readonly MethodInfo s_run = typeof(RemoteClient)
    .GetMethod("RunLauncher", BindingFlags.NonPublic | BindingFlags.Static)!;

  private object? _previousRuntime;
  private string _previousDirectory = null!;
  private string _directory = null!;

  [SetUp]
  public void SetUp()
  {
    _directory = Path.Combine(Path.GetTempPath(), "mtgosdk-launcher-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_directory);
    _previousRuntime = s_runtime.GetValue(null);
    _previousDirectory = Bootstrapper.ExtractDir;
    Bootstrapper.ExtractDir = _directory;
    Bootstrapper.Configure(new ShellRuntime());
  }

  [TearDown]
  public void TearDown()
  {
    s_runtime.SetValue(null, _previousRuntime);
    Bootstrapper.ExtractDir = _previousDirectory;
    Directory.Delete(_directory, recursive: true);
  }

  [Test]
  public async Task Test_FinalProgress_IsDeliveredBeforeCompletion()
  {
    LaunchProgress? received = null;
    await Run(ProgressCommand(), new Receiver(update => received = update));

    Assert.That(received?.Stage, Is.EqualTo(LaunchStage.Started));
  }

  [Test]
  public async Task Test_ReceiverFailure_DoesNotFailTheLauncher()
  {
    bool received = false;
    await Run(ProgressCommand(), new Receiver(_ =>
    {
      received = true;
      throw new InvalidOperationException("The receiver failed.");
    }));

    Assert.That(received, Is.True, "The launcher must actually report progress in this scenario");
  }

  [Test]
  public async Task Test_ProgressOutput_PreservesUnicodeAcrossTheProcessPipe()
  {
    string path = Path.Combine(_directory, "progress.json");
    File.WriteAllText(path, "{\"stage\":6,\"message\":\"Déploiement 日本語\"}\n", new UTF8Encoding(false));
    string arguments = OperatingSystem.IsWindows()
      ? $"/d /c type \"{path}\"" : $"-c \"cat '{path}'\"";
    LaunchProgress? received = null;
    await Run(arguments, new Receiver(update => received = update));

    Assert.That(received?.Message, Is.EqualTo("Déploiement 日本語"));
  }

  [Test]
  public async Task Test_NonzeroExit_IsReportedToTheCaller()
  {
    string arguments = OperatingSystem.IsWindows() ? "/d /c exit 7" : "-c \"exit 7\"";
    await Assert.ThatAsync(() => Run(arguments),
      Throws.TypeOf<ExternalErrorException>().With.Message.Contains("exited with code 7"));
  }

  [Test]
  public async Task Test_Timeout_AlsoBoundsFinalOutputDelivery()
  {
    using var release = new ManualResetEventSlim();
    var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    Task operation = Run(ProgressCommand(), new Receiver(_ =>
    {
      entered.TrySetResult(true);
      release.Wait();
      finished.TrySetResult(true);
    }), timeout: TimeSpan.FromSeconds(1));

    try
    {
      await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
      await Assert.ThatAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.TypeOf<ExternalErrorException>().With.Message.Contains("timed out"));
    }
    finally
    {
      release.Set();
      await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
  }

  [Test]
  public async Task Test_Timeout_TerminatesTheLaunchedHelper()
  {
    string script = Path.Combine(_directory, "wait.cmd");
    File.WriteAllText(script, "@echo off\r\n:wait\r\ngoto wait\r\n");
    string arguments = OperatingSystem.IsWindows()
      ? $"/d /c \"{script}\"" : "-c \"while :; do :; done\"";
    Task operation = Run(arguments, timeout: TimeSpan.FromSeconds(2));
    Process? candidate = null;
    for (int attempt = 0; attempt < 100 && candidate is null; attempt++)
    {
      candidate = FindHelper();
      if (candidate is null) await Task.Delay(10);
    }
    using var helper = candidate;
    try
    {
      Assert.That(helper, Is.Not.Null, "The helper must be running before its timeout expires");
      await Assert.ThatAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(10)),
        Throws.TypeOf<ExternalErrorException>().With.Message.Contains("timed out after 2 seconds"));
      Assert.That(helper!.HasExited, Is.True, "A timed-out helper must not continue updating MTGO");
    }
    finally
    {
      if (helper is not null && !helper.HasExited)
      {
        helper.Kill();
        await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
      }
    }
  }

  private static Task Run(string arguments, IProgress<LaunchProgress>? progress = null, TimeSpan? timeout = null) =>
    (Task)s_run.Invoke(null, [arguments, timeout ?? TimeSpan.FromSeconds(10), progress])!;

  private static string ProgressCommand() => OperatingSystem.IsWindows()
    ? "/d /c echo {\"stage\":5}"
    : "-c \"printf '\\173\\042stage\\042:5\\175\\n'\"";

  private Process? FindHelper()
  {
    if (!OperatingSystem.IsWindows())
    {
      string pidFile = Path.Combine(_directory, "Launcher.exe.pid");
      if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile), out int pid)) return null;

      return Process.GetProcessById(pid);
    }

    foreach (Process process in Process.GetProcessesByName("Launcher"))
    {
      try
      {
        string? directory = Path.GetDirectoryName(process.MainModule?.FileName);
        if (Path.GetFileName(directory) == Path.GetFileName(_directory)) return process;
      }
      catch (InvalidOperationException) { }
      catch (Win32Exception) { }

      process.Dispose();
    }

    return null;
  }

  private sealed class Receiver(Action<LaunchProgress> report) : IProgress<LaunchProgress>
  {
    public void Report(LaunchProgress value) => report(value);
  }

  private sealed class ShellRuntime : IBootstrapperRuntime
  {
    public byte[] GetBinaryResource(string name) => OperatingSystem.IsWindows()
      ? File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
      : Encoding.UTF8.GetBytes("#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\nexec /bin/sh \"$@\"\n");

    public void OverrideFileIfChanged(string path, byte[] data)
    {
      File.WriteAllBytes(path, data);
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Inject(Process target, ushort diverPort) =>
      throw new InvalidOperationException("Launcher process tests must not inject into MTGO.");
  }
}
