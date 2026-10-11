/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// A native type identity, resolved in the target process.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchType
{
  [Key(0)]
  public string FullName { get; set; } = "";
  [Key(1)]
  public string? AssemblyName { get; set; }
  [Key(2)]
  public RemoteBatchType[] GenericArguments { get; set; } = [];
  [Key(3)]
  public RemoteBatchType? ElementType { get; set; }
  [Key(4)]
  public int ArrayRank { get; set; }
  [Key(5)]
  public bool IsByRef { get; set; }
  [Key(6)]
  public bool IsVector { get; set; }

  public RemoteBatchType() { }
  public RemoteBatchType(string fullName, string? assemblyName = null)
  {
    FullName = fullName;
    AssemblyName = assemblyName;
  }

  /// <summary>
  /// Describes a type signature from local metadata. Core-library assembly names
  /// are omitted so those types can resolve against the target process's CLR.
  /// </summary>
  public static RemoteBatchType From(Type type) => Describe(type, portable: true);
  internal static RemoteBatchType Native(Type type) => Describe(type, portable: false);

  private static RemoteBatchType Describe(Type type, bool portable)
  {
    if (type.IsPointer || type.IsGenericParameter)
      throw new NotSupportedException("Pointer and open generic parameter identities are unsupported.");
    var result = new RemoteBatchType(type.IsConstructedGenericType
      ? type.GetGenericTypeDefinition().FullName! : type.FullName!,
      portable && type.Assembly == typeof(object).Assembly ? null : type.Assembly.GetName().Name);
    if (type.IsConstructedGenericType)
      result.GenericArguments = type.GetGenericArguments().Select(t => Describe(t, portable)).ToArray();
    if (type.HasElementType)
    {
      var element = type.GetElementType()!;
      result.ElementType = Describe(element, portable);
      result.IsByRef = type.IsByRef;
      result.ArrayRank = type.IsArray ? type.GetArrayRank() : 0;
      result.IsVector = type.IsArray && type == element.MakeArrayType();
    }
    return result;
  }
}
