/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MessagePack;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private static readonly byte[] s_pongResponse =
    WrapSuccess(new StatusResponse { Status = "pong" });

  private byte[] MakePingResponse(HttpListenerRequest arg) => s_pongResponse;
}

[MessagePackObject]
public class StatusResponse
{
  [Key(0)]
  public string Status { get; set; }
}
