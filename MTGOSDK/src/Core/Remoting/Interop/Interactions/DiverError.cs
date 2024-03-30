/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

public class DiverError(string err, string stackTrace)
{
  public string Error { get; set; } = err;
  public string StackTrace { get; set; } = stackTrace;
}
