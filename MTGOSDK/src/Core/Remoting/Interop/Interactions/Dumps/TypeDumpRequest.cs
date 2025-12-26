/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class TypeDumpRequest
{
  [Key(0)]
  public string Assembly { get; set; }
  [Key(1)]
  public string TypeFullName { get; set; }
}
