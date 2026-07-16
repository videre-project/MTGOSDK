/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using MTGOSDK.Win32.Injection;

using static MTGOSDK.Resources.EmbeddedResources;


namespace MTGOSDK.Resources;

internal sealed class HostBootstrapperRuntime : IBootstrapperRuntime
{
  public byte[] GetBinaryResource(string name) =>
    EmbeddedResources.GetBinaryResource(name);

  public void OverrideFileIfChanged(string filePath, byte[] data) =>
    EmbeddedResources.OverrideFileIfChanged(filePath, data);

  public void Inject(Process target, ushort diverPort)
  {
    // Create the extraction directory if it doesn't exist
    DirectoryInfo AppDataDirInfo = new DirectoryInfo(Bootstrapper.AppDataDir);
    if (!AppDataDirInfo.Exists) AppDataDirInfo.Create();

    // Create a random temporary directory to be deleted when the process exits
    string tempDir = Path.Combine(Bootstrapper.AppDataDir, Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    AppDomain.CurrentDomain.ProcessExit += delegate
    {
      try
      {
        Directory.Delete(tempDir, true);
      }
      catch (UnauthorizedAccessException)
      {
        // Given we had permissions to write this directory, we assume this
        // is caused by a file lock from the MTGO process (if it's still running).
      }
    };

    // Get the .NET diver assembly to inject into the target process
    byte[] diverResource = GetBinaryResource(@"Resources/Microsoft.Diagnostics.Runtime.dll");
    string diverPath = Path.Combine(tempDir, "Microsoft.Diagnostics.Runtime.dll");

    // Check if injector or bootstrap resources differ from copies on disk
    OverrideFileIfChanged(diverPath, diverResource);

    // System.ValueTuple is not available in the MTGO process's GAC/probing paths.
    // Extract it alongside the Diver so the AssemblyResolve handler in DllEntry
    // can supply it when the CLR requests it after injection.
    byte[] valueTupleResource = GetBinaryResource(@"Resources/System.ValueTuple.dll");
    string valueTuplePath = Path.Combine(tempDir, "System.ValueTuple.dll");
    OverrideFileIfChanged(valueTuplePath, valueTupleResource);

    // Verify all files are fully written before injection
    VerifyDiverPayload(diverPath);
    VerifyFileExists(valueTuplePath, "System.ValueTuple.dll");

    var injector = new InjectorBase();
    injector.Inject(target, diverPath, "ScubaDiver.DllEntry", "EntryPoint", diverPort.ToString());
  }

  /// <summary>
  /// Verifies that a file exists on disk, with retry logic for non-deterministic
  /// file availability issues that can occur after extraction.
  /// </summary>
  private static void VerifyFileExists(string filePath, string fileName)
  {
    const int maxRetries = 10;
    const int delayMs = 50;

    for (int i = 0; i < maxRetries; i++)
    {
      if (File.Exists(filePath))
      {
        // Verify file is not empty (indicates incomplete write)
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > 0)
          return;
      }
      Thread.Sleep(delayMs);
    }

    throw new FileNotFoundException(
      $"Failed to verify {fileName} exists after extraction. " +
      $"Path: {filePath}");
  }

  private static void VerifyDiverPayload(string filePath)
  {
    const string expectedAssemblyName = "Microsoft.Diagnostics.Runtime";

    VerifyFileExists(filePath, expectedAssemblyName + ".dll");

    string? assemblyName;
    try
    {
      assemblyName = AssemblyName.GetAssemblyName(filePath).Name;
    }
    catch (Exception e) when (
      e is BadImageFormatException ||
      e is FileLoadException ||
      e is FileNotFoundException)
    {
      throw new InvalidDataException(
        $"The ScubaDiver payload at '{filePath}' is not a valid managed assembly.",
        e);
    }

    if (!string.Equals(
      assemblyName,
      expectedAssemblyName,
      StringComparison.Ordinal))
    {
      throw new InvalidDataException(
        "The ScubaDiver payload is not the merged injection assembly. " +
        $"Expected '{expectedAssemblyName}', found '{assemblyName ?? "<unknown>"}'. " +
        "Build ScubaDiver with UseILRepack=true before running MTGOSDK.");
    }

    if (!ContainsTypeDefinition(filePath, "HarmonyLib", "Harmony"))
    {
      throw new InvalidDataException(
        "The ScubaDiver payload does not contain the merged Harmony runtime. " +
        "Build ScubaDiver with UseILRepack=true before running MTGOSDK.");
    }
  }

  private static bool ContainsTypeDefinition(
    string filePath,
    string @namespace,
    string name)
  {
    using FileStream stream = File.OpenRead(filePath);
    using PEReader peReader = new(stream);
    if (!peReader.HasMetadata)
      return false;

    MetadataReader metadata = peReader.GetMetadataReader();
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
      TypeDefinition type = metadata.GetTypeDefinition(handle);
      if (metadata.StringComparer.Equals(type.Namespace, @namespace) &&
          metadata.StringComparer.Equals(type.Name, name))
      {
        return true;
      }
    }

    return false;
  }
}

internal static class BootstrapperRuntimeInitializer
{
#pragma warning disable CA2255
  [ModuleInitializer]
  internal static void Initialize() =>
    Bootstrapper.Configure(new HostBootstrapperRuntime());
#pragma warning restore CA2255
}
