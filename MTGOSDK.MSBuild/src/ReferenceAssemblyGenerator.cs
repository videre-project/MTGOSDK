/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using JetBrains.Refasmer;
using JetBrains.Refasmer.Filters;


namespace MTGOSDK.MSBuild;

public static class ReferenceAssemblyGenerator
{
  public static unsafe byte[] Convert(
    string filepath,
    IImportFilter? filter = null,
    ILogger? logger = null)
  {
    using PEReader reader = new(new FileStream(
      filepath,
      FileMode.Open,
      FileAccess.Read
    ));
    return Convert(reader, filter, logger);
  }

  public static unsafe byte[] Convert(
    byte[] asm,
    IImportFilter? filter = null,
    ILogger? logger = null)
  {
    fixed (byte* be = asm)
    {
      using PEReader reader = new(be, asm.Length);
      return Convert(reader, filter, logger);
    }
  }

  public static unsafe byte[] Convert(
    PEReader reader,
    IImportFilter? filter = null,
    ILogger? logger = null)
  {
    logger ??= _logger;
    MetadataReader metadata = reader.GetMetadataReader();
    var asm = MetadataImporter.MakeRefasm(
      metadata,
      reader,
      new LoggerBase(logger),
      filter,
      //
      // This generates a 'reference assembly' that can be loaded at runtime.
      //
      // This isn't an assembly with real implementation code, and will raise
      // a 'NotImplementedException' when calling any of it's generated methods.
      //
      false, /* MakeMock */
      true   /* OmitReferenceAssemblyAttr */
    );
    ZeroOutInvalidRVAs(asm, logger);
    return asm;
  }

  /// <summary>
  /// Manually zeroes out RVAs for methods that should not have them.
  /// </summary>
  /// <remarks>
  /// Refasmer sometimes generates non-zero RVAs for Runtime/InternalCall methods
  /// (especially on Delegates). Wine's .NET runtime strictly forbids this.
  /// </remarks>
  private static unsafe void ZeroOutInvalidRVAs(byte[] asm, ILogger logger)
  {
    fixed (byte* pAsm = asm)
    {
      using var reader = new PEReader(pAsm, asm.Length);
      if (!reader.HasMetadata) return;

      var metadata = reader.GetMetadataReader();
      var metadataBlock = reader.GetMetadata();
      int methodDefOffset = (int)(metadataBlock.Pointer - pAsm) + metadata.GetTableMetadataOffset(TableIndex.MethodDef);
      int methodDefRowSize = metadata.GetTableRowSize(TableIndex.MethodDef);

      foreach (var handle in metadata.MethodDefinitions)
      {
        var method = metadata.GetMethodDefinition(handle);
        if (method.RelativeVirtualAddress != 0)
        {
          //
          // Check if this method should have an RVA.
          //
          // In a reference assembly, almost nothing should have an RVA,
          // but Wine (on Linux/macOS) is particularly sensitive to Runtime/InternalCall
          // methods having them (which happens for Delegate.Invoke etc).
          //
          bool isRuntime = (method.ImplAttributes & System.Reflection.MethodImplAttributes.CodeTypeMask) != 0 ||
                           (method.ImplAttributes & (System.Reflection.MethodImplAttributes.Runtime |
                                                     System.Reflection.MethodImplAttributes.InternalCall |
                                                     System.Reflection.MethodImplAttributes.Native)) != 0;

          bool isAbstract = (method.Attributes & System.Reflection.MethodAttributes.Abstract) != 0;

          if (isRuntime || isAbstract)
          {
            // Zero out the RVA field (first 4 bytes of the MethodDef row)
            int rowOffset = methodDefOffset + (metadata.GetRowNumber(handle) - 1) * methodDefRowSize;
            logger.Log(JetBrains.Refasmer.LogLevel.Warning, $"Stripping RVA 0x{method.RelativeVirtualAddress:X} from method {metadata.GetString(method.Name)} (Attribs: {method.Attributes}, Impl: {method.ImplAttributes})");
            for (int i = 0; i < 4; i++)
              asm[rowOffset + i] = 0;
          }
        }
      }
    }
  }

  private static readonly ILogger _logger = new DisableLogging();

  private class DisableLogging : ILogger
  {
    public void Log(LogLevel logLevel, string message) {}
    public bool IsEnabled(LogLevel logLevel) => false;
  }

  internal class TaskLogger : ILogger
  {
    private readonly Microsoft.Build.Utilities.TaskLoggingHelper _log;
    public TaskLogger(Microsoft.Build.Utilities.TaskLoggingHelper log) => _log = log;
    public void Log(LogLevel logLevel, string message)
    {
      var importance = logLevel switch
      {
        LogLevel.Error => Microsoft.Build.Framework.MessageImportance.High,
        LogLevel.Warning => Microsoft.Build.Framework.MessageImportance.High,
        _ => Microsoft.Build.Framework.MessageImportance.Normal
      };
      _log.LogMessage(importance, message);
    }
    public bool IsEnabled(LogLevel logLevel) => true;
  }
}
