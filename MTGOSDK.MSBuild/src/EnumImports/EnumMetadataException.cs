/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

namespace MTGOSDK.MSBuild;

/// <summary>
/// Associates a metadata lookup failure with the requested enum type.
/// </summary>
internal sealed class EnumMetadataException : InvalidOperationException
{
  public string TypeName { get; }

  public EnumMetadataException(string typeName, string message)
    : base(message)
  {
    TypeName = typeName;
  }
}
