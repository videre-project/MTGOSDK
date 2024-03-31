/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Security;


Console.WriteLine($"Connecting to MTGO v{Client.Version}...");
using var client = new Client(
  RemoteClient.HasStarted
    ? new ClientOptions()
    : new ClientOptions
      {
        CreateProcess = true,
        // DestroyOnExit = true,
        AcceptEULAPrompt = true
      }
);

if (!client.IsConnected)
{
  DotEnv.LoadFile();
  await client.LogOn(
    username: DotEnv.Get("USERNAME"),
    password: DotEnv.Get("PASSWORD")
  );
}

Console.WriteLine($"Finished loading.");
