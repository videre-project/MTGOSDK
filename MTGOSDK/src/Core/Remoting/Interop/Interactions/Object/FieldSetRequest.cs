/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

public class FieldSetRequest
{
  public ulong ObjAddress { get; set; }
  public string TypeFullName { get; set; }
  public string FieldName { get; set; }
  public ObjectOrRemoteAddress Value { get; set; }
}
