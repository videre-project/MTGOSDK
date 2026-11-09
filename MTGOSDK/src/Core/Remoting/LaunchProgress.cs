/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;


namespace MTGOSDK.Core.Remoting;

/// <summary>
/// Stages in the MTGO update and startup process.
/// </summary>
public enum LaunchStage
{
  /// <summary>Checking the installed deployment or requesting a manifest.</summary>
  CheckingForUpdates = 0,

  /// <summary>Verifying application requirements before installation.</summary>
  VerifyingRequirements = 1,

  /// <summary>Downloading MTGO application files.</summary>
  Downloading = 2,

  /// <summary>Installation or updating has completed.</summary>
  UpdateCompleted = 3,

  /// <summary>Activating the installed MTGO deployment.</summary>
  Starting = 4,

  /// <summary>MTGO has opened its application window.</summary>
  Started = 5,

  /// <summary>The launcher could not complete the requested operation.</summary>
  Failed = 6
}

/// <summary>
/// Reports MTGO update and startup progress.
/// </summary>
[DataContract]
public sealed class LaunchProgress
{
  /// <summary>
  /// The current update or startup stage.
  /// </summary>
  [DataMember(Name = "stage", IsRequired = true)]
  public LaunchStage Stage { get; private set; }

  /// <summary>
  /// Download percentage, when supplied by ClickOnce.
  /// </summary>
  [DataMember(Name = "percentage", EmitDefaultValue = false)]
  public int? Percentage { get; private set; }

  /// <summary>
  /// The number of bytes downloaded.
  /// </summary>
  [DataMember(Name = "bytesDownloaded", EmitDefaultValue = false)]
  public long? BytesDownloaded { get; private set; }

  /// <summary>
  /// The expected download size, when known.
  /// </summary>
  [DataMember(Name = "totalBytes", EmitDefaultValue = false)]
  public long? TotalBytes { get; private set; }

  /// <summary>
  /// The deployment version supplied by ClickOnce.
  /// </summary>
  [DataMember(Name = "version", EmitDefaultValue = false)]
  public string? Version { get; private set; }

  /// <summary>
  /// Additional status or failure information.
  /// </summary>
  [DataMember(Name = "message", EmitDefaultValue = false)]
  public string? Message { get; private set; }

  /// <summary>
  /// Creates a progress report for the given stage.
  /// </summary>
  public LaunchProgress(
    LaunchStage stage,
    int? percentage = null,
    long? bytesDownloaded = null,
    long? totalBytes = null,
    string? version = null,
    string? message = null)
  {
    Stage = stage;
    Percentage = percentage;
    BytesDownloaded = bytesDownloaded;
    TotalBytes = totalBytes;
    Version = version;
    Message = message;
  }
}

// Shared with Launcher so process output and SDK progress use the same contract.
internal static class LaunchProgressProtocol
{
  public static string Serialize(LaunchProgress progress)
  {
    using var stream = new MemoryStream();
    new DataContractJsonSerializer(typeof(LaunchProgress)).WriteObject(stream, progress);

    return Encoding.UTF8.GetString(stream.ToArray());
  }

  public static bool TryRead(string line, out LaunchProgress? progress)
  {
    progress = null;
    if (!line.StartsWith("{", StringComparison.Ordinal)) return false;

    try
    {
      using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
      var value = (LaunchProgress?)new DataContractJsonSerializer(typeof(LaunchProgress))
        .ReadObject(stream);
      if (value is null || !Enum.IsDefined(typeof(LaunchStage), value.Stage))
        return false;

      progress = value;
      return true;
    }
    catch (SerializationException)
    {
      return false;
    }
  }
}
