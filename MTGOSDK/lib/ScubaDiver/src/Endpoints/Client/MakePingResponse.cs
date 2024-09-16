/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  #region Ping Handler

  private string MakePingResponse(HttpListenerRequest arg)
  {
    return "{\"status\":\"pong\"}";
  }

  #endregion
}
