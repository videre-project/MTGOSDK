/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Reflection;
using System.Xml;


namespace MTGOSDK.Resources;

/// <summary>
/// Provides access to embedded resources in the MTGOSDK assembly.
/// </summary>
public static class EmbeddedResources
{
  private static readonly Assembly asm =
    Assembly.GetAssembly(typeof(EmbeddedResources));

  /// <summary>
  /// Provides a stream to the specified embedded resource.
  /// </summary>
  /// <param name="name">The name of the resource.</param>
  /// <returns>A stream to the resource.</returns>
  public static Stream GetResourceStream(string name)
  {
    return asm.GetManifestResourceStream(name);
  }

  /// <summary>
  /// Provides a binary representation of the specified embedded resource.
  /// </summary>
  /// <param name="name">The name of the resource.</param>
  /// <returns>A byte array containing the resource.</returns>
  public static byte[] GetBinaryResource(string name)
  {
    using (var stream = GetResourceStream(name))
    {
      if (stream == null)
      {
        throw new FileNotFoundException($"Resource {name} not found.");
      }

      var buffer = new byte[stream.Length];
#if NET9_0_OR_GREATER
      stream.ReadExactly(buffer);
#else
      stream.Read(buffer, 0, buffer.Length);
#endif

      return buffer;
    }
  }

  /// <summary>
  /// Provides an XML representation of the specified embedded resource.
  /// </summary>
  /// <param name="name">The name of the resource.</param>
  /// <returns>An XML document containing the resource.</returns>
  public static XmlDocument GetXMLResource(string name)
  {
    var doc = new XmlDocument();
    using (var reader = new StreamReader(GetResourceStream(name)))
    {
      doc.LoadXml(reader.ReadToEnd());
    }

    return doc;
  }

  public static void OverrideFileIfChanged(string filePath, byte[] data)
  {
    bool fileChanged = true;

    // If the parent directories don't exist, create them recursively.
    var parentDir = Path.GetDirectoryName(filePath);
    if (!Directory.Exists(parentDir))
    {
      Directory.CreateDirectory(parentDir);
    }

    if (File.Exists(filePath))
    {
      using (FileStream file = new(filePath, FileMode.Open, FileAccess.Read))
      {
        if (file.Length == data.Length)
        {
          fileChanged = false;
          for (int i = 0; i < file.Length; i++)
          {
            if (file.ReadByte() != data[i])
            {
              fileChanged = true;
              break;
            }
          }
        }
      }
    }

    if (fileChanged)
    {
      File.WriteAllBytes(filePath, data);
    }
  }
}
