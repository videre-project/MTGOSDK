/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Microsoft.CSharp;

namespace MTGOSDK.MSBuild;

/// <summary>
/// Reads enum member names and values from assembly metadata without loading
/// the client assemblies into the build process.
/// </summary>
public static class EnumMetadata
{
  public static IReadOnlyDictionary<string, string> ReadMembers(
    IEnumerable<string> references, IEnumerable<string> typeNames)
  {
    using var provider = new CSharpCodeProvider();
    var wanted = new HashSet<string>(typeNames, StringComparer.Ordinal);
    var found = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var path in references.Select(Path.GetFullPath).Distinct())
    {
      using var stream = File.OpenRead(path);
      using var pe = new PEReader(stream);
      if (!pe.HasMetadata)
        continue;

      var reader = pe.GetMetadataReader();
      foreach (var handle in reader.TypeDefinitions)
      {
        var name = TypeName(reader, handle);
        if (!wanted.Contains(name))
          continue;
        if (found.ContainsKey(name))
          throw new EnumMetadataException(name, $"Enum '{name}' is ambiguous in the resolved references.");

        var type = reader.GetTypeDefinition(handle);
        if (TypeName(reader, type.BaseType) != "System.Enum")
          throw new EnumMetadataException(name, $"Referenced type '{name}' is not an enum.");

        var members = new List<string>();
        foreach (var fieldHandle in type.GetFields())
        {
          var field = reader.GetFieldDefinition(fieldHandle);
          var constantHandle = field.GetDefaultValue();
          if (constantHandle.IsNil)
            continue;

          var constant = reader.GetConstant(constantHandle);
          var blob = reader.GetBlobReader(constant.Value);
          var value = blob.ReadConstant(constant.TypeCode);
          var member = reader.GetString(field.Name);
          members.Add(provider.CreateEscapedIdentifier(member) + " = " + Format(value) + ",");
        }

        found.Add(name, string.Join("\n", members));
      }
    }

    foreach (var name in wanted)
      if (!found.ContainsKey(name))
        throw new EnumMetadataException(name, $"Enum '{name}' was not found in the resolved references.");

    return found;
  }

  private static string TypeName(MetadataReader reader, EntityHandle handle)
  {
    if (handle.IsNil)
      return string.Empty;

    if (handle.Kind == HandleKind.TypeDefinition)
    {
      var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
      var parent = type.GetDeclaringType();
      return parent.IsNil
        ? Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name))
        : TypeName(reader, parent) + "+" + reader.GetString(type.Name);
    }

    if (handle.Kind == HandleKind.TypeReference)
    {
      var type = reader.GetTypeReference((TypeReferenceHandle)handle);
      return Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    return string.Empty;
  }

  private static string Qualify(string ns, string name) =>
    string.IsNullOrEmpty(ns) ? name : ns + "." + name;

  private static string Format(object? value) => value switch
  {
    sbyte or byte or short or ushort or int => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
    long number when number == long.MinValue => "global::System.Int64.MinValue",
    long number => number.ToString(CultureInfo.InvariantCulture) + "L",
    ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
    _ => throw new InvalidOperationException("An enum member has a non-integral constant.")
  };
}
