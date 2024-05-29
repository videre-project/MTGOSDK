/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using JetBrains.Refasmer;
using JetBrains.Refasmer.Filters;


namespace MTGOSDK.MSBuild;

public static class ReferenceAssemblyGenerator
{
  public static unsafe byte[] Convert(
    string filepath,
    IImportFilter? filter = null)
  {
    using PEReader reader = new(new FileStream(
      filepath,
      FileMode.Open,
      FileAccess.Read
    ));
    return Convert(reader, filter);
  }

  public static unsafe byte[] Convert(
    byte[] asm,
    IImportFilter? filter = null)
  {
    fixed (byte* be = asm)
    {
      using PEReader reader = new(be, asm.Length);
      return Convert(reader, filter);
    }
  }

  public static unsafe byte[] Convert(
    PEReader reader,
    IImportFilter? filter = null)
  {
    MetadataReader metadata = reader.GetMetadataReader();
    return MetadataImporter.MakeRefasm(
      metadata,
      reader,
      _logger,
      filter,
      //
      // This generates a 'reference assembly' that can be loaded at runtime.
      //
      // This isn't an assembly with real implementation code, and will raise
      // a 'NotImplementedException' when calling any of it's generated methods.
      //
      true, /* MakeMock */
      true  /* OmitReferenceAssemblyAttr */
    );
  }

  private static readonly LoggerBase _logger = new(new DisableLogging());

  private class DisableLogging : ILogger
  {
    public void Log(LogLevel logLevel, string message) {}
    public bool IsEnabled(LogLevel logLevel) => false;
  }
}
