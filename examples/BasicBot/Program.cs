/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API;
using MTGOSDK.Core.Security;


Console.WriteLine($"Connecting to MTGO v{Client.Version}...");
var client = new Client(
  new ClientOptions { CreateProcess = true, AcceptEULAPrompt = true }
);

DotEnv.LoadFile();
client.LogOn(
  username: DotEnv.Get("USERNAME"),
  password: DotEnv.Get("PASSWORD")
);
