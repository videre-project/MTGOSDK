/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;


namespace MTGOSDK.Core.Reflection.Serialization;

public struct PropertyFilter
{
  public readonly IList<string> Includes;
  public readonly IList<string> Excludes;
  public readonly bool Strict;

  public readonly IList<PropertyInfo> Properties;

  public PropertyFilter(Type derivedType)
      : this([], [], derivedType: derivedType)
  {
  }

  public PropertyFilter(
    IList<string>? include,
    IList<string>? exclude,
    bool strict = false,
    Type? derivedType = null)
  {
    this.Includes = include ?? [];
    this.Excludes = exclude ?? [];
    this.Strict = strict;

    Properties = GetSerializableProperties(derivedType);
  }

  public bool IsSerializable(PropertyInfo property)
  {
    if (Strict) return Includes.Contains(property.Name);

    return property.GetCustomAttribute<NonSerializableAttribute>() == null &&
           property.GetGetMethod()?.IsPublic == true &&
          !Excludes.Contains(property.Name) ||
           Includes.Contains(property.Name);
  }

  public IList<PropertyInfo> GetSerializableProperties(Type derivedType)
  {
    var self = this; // Capture 'this' to avoid CS8176
    // Get all properties of the object, public and non-public.
    return derivedType
      .GetProperties(
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
      // Only include public properties or those with a JsonInclude attribute;
      // exclude all properties with a JsonIgnore attribute.
      .Where(p => self.IsSerializable(p))
      .OrderBy(p => p.MetadataToken)
      .ToList();
  }
}
