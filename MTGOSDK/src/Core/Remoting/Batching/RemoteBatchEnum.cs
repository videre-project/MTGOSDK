/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Dynamic;

using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// A captured native enum, retaining its identity, number, and display name.
/// </summary>
public sealed class RemoteBatchEnum(RemoteBatchType type, object enumValue, string? name)
  : DynamicObject, IEquatable<RemoteBatchEnum>
{
  public RemoteBatchType Type { get; } = type;
  public object EnumValue { get; } = enumValue;
  public string? Name { get; } = name;
  public override bool TryConvert(ConvertBinder binder, out object? result)
  {
    if (binder.Type.IsEnum)
    {
      result = Enum.ToObject(binder.Type, EnumValue);
      return true;
    }
    result = null;
    return false;
  }
  public override string ToString() => Name ?? Convert.ToString(EnumValue,
    System.Globalization.CultureInfo.InvariantCulture)!;

  public bool Equals(RemoteBatchEnum? other) => other != null &&
    Type.FullName == other.Type.FullName && Type.AssemblyName == other.Type.AssemblyName &&
    Equals(EnumValue, other.EnumValue) && Name == other.Name;
  public override bool Equals(object? obj) => obj is RemoteBatchEnum other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(Type.FullName, Type.AssemblyName, EnumValue, Name);
}
