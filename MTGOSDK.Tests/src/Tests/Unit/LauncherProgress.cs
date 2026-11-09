/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Text.Json;

using MTGOSDK.Core.Remoting;


namespace MTGOSDK.UnitTests;

/// <summary>
/// Launcher stdout mixes diagnostic lines with progress records consumed by
/// the SDK and native hosts. Field names and stage values must remain compatible.
/// </summary>
public class LauncherProgress
{
  private static readonly Type s_protocol = typeof(LaunchProgress).Assembly
    .GetType("MTGOSDK.Core.Remoting.LaunchProgressProtocol", throwOnError: true)!;

  // Exercise the packaged protocol without exposing it as a public SDK API.
  private static bool TryRead(string line, out LaunchProgress? progress)
  {
    object?[] arguments = [line, null];
    bool result = (bool)s_protocol.GetMethod("TryRead")!.Invoke(null, arguments)!;
    progress = (LaunchProgress?)arguments[1];

    return result;
  }

  private static string Serialize(LaunchProgress progress) =>
    (string)s_protocol.GetMethod("Serialize")!.Invoke(null, [progress])!;

  [Test]
  public void Test_DownloadRecord_Preserves64BitSizesAndDeploymentVersion()
  {
    const string line = "{\"stage\":2,\"percentage\":50,\"bytesDownloaded\":5000000000," +
      "\"totalBytes\":10000000000,\"version\":\"1.2.3.4\"}";

    Assert.That(TryRead(line, out var progress), Is.True);
    Assert.That(progress, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(progress!.Stage, Is.EqualTo(LaunchStage.Downloading));
      Assert.That(progress.Percentage, Is.EqualTo(50));
      Assert.That(progress.BytesDownloaded, Is.EqualTo(5000000000L));
      Assert.That(progress.TotalBytes, Is.EqualTo(10000000000L));
      Assert.That(progress.Version, Is.EqualTo("1.2.3.4"));
    });
  }

  [Test]
  public void Test_MinimalRecord_LeavesUnknownDetailsUnset()
  {
    Assert.That(TryRead("{\"stage\":4}", out var progress), Is.True);
    Assert.That(progress, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(progress!.Stage, Is.EqualTo(LaunchStage.Starting));
      Assert.That(progress.Percentage, Is.Null);
      Assert.That(progress.BytesDownloaded, Is.Null);
      Assert.That(progress.TotalBytes, Is.Null);
      Assert.That(progress.Version, Is.Null);
      Assert.That(progress.Message, Is.Null);
    });
  }

  [Test]
  public void Test_FailureRecord_AllowsFutureFieldsAndUnicodeMessages()
  {
    const string line = "{\"stage\":6,\"message\":\"Could not open Déploiement\",\"futureField\":true}";

    Assert.That(TryRead(line, out var progress), Is.True);
    Assert.That(progress, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(progress!.Stage, Is.EqualTo(LaunchStage.Failed));
      Assert.That(progress.Message, Is.EqualTo("Could not open Déploiement"));
    });
  }

  [Test]
  public void Test_Writer_UsesNumericStagesAndOmitsUnavailableFields()
  {
    using var document = JsonDocument.Parse(Serialize(new LaunchProgress(
      LaunchStage.CheckingForUpdates, version: "1.2.3.4")));
    var record = document.RootElement;

    Assert.Multiple(() =>
    {
      Assert.That(record.GetProperty("stage").GetInt32(), Is.EqualTo(0));
      Assert.That(record.GetProperty("version").GetString(), Is.EqualTo("1.2.3.4"));
      Assert.That(record.TryGetProperty("percentage", out _), Is.False);
      Assert.That(record.TryGetProperty("bytesDownloaded", out _), Is.False);
      Assert.That(record.TryGetProperty("totalBytes", out _), Is.False);
      Assert.That(record.TryGetProperty("message", out _), Is.False);
    });
  }

  [TestCase("Checking for MTGO updates")]
  [TestCase("{not JSON}")]
  [TestCase("{}")]
  [TestCase("{\"stage\":999}")]
  [TestCase("{\"stage\":\"Downloading\"}")]
  [TestCase("{\"stage\":2,\"bytesDownloaded\":\"invalid\"}")]
  public void Test_NonProgressOutput_DoesNotProduceAnUpdate(string line)
  {
    Assert.That(TryRead(line, out var progress), Is.False);
    Assert.That(progress, Is.Null);
  }
}
