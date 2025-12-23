/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

[MessagePackObject]
public class DiverError
{
  [Key(0)]
  public string Error { get; set; }
  [Key(1)]
  public string StackTrace { get; set; }

  public DiverError() { }

  public DiverError(string err, string stackTrace)
  {
    Error = err;
    StackTrace = stackTrace;
  }
}
