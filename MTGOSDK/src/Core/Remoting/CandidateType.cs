/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

namespace MTGOSDK.Core.Remoting;

public class CandidateType(string typeName, string assembly)
{
  public string TypeFullName = typeName;
  public string Assembly = assembly;
}
