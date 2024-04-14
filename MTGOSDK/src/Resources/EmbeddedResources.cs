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
      stream.Read(buffer, 0, buffer.Length);

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
}
