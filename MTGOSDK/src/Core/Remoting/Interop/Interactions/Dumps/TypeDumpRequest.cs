/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

public class TypeDumpRequest
{
  public string Assembly { get; set; }
  public string TypeFullName { get; set; }
}
