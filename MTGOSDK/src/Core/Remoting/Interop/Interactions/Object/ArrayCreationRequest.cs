/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

[MessagePackObject]
public class ArrayCreationRequest
{
  [Key(0)]
  public string ElementTypeFullName { get; set; }
  [Key(1)]
  public int Length { get; set; }
  /// <summary>
  /// Optional: Constructor arguments for each element.
  /// If provided, Length is ignored and the array size is determined by this list.
  /// Each inner list contains the constructor arguments for one element.
  /// </summary>
  [Key(2)]
  public List<List<ObjectOrRemoteAddress>> ConstructorArgs { get; set; }
}

