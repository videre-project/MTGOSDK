/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;

using MTGOSDK.API;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Security;


// Wait until the main MTGO server is online.
bool restart = false;
while (!await ServerStatus.IsOnline())
{
  Console.WriteLine("MTGO servers are currently offline. Waiting...");
  await Task.Delay(TimeSpan.FromMinutes(30));
  restart |= true; // Restart after downtime.
}

// Initialize the client instance.
Console.WriteLine($"Connecting to MTGO v{Client.CompatibleVersion}...");
using var client = new Client(
  !restart && Client.HasStarted
    ? new ClientOptions()
    : new ClientOptions
      {
        CreateProcess = true,
        DestroyOnExit = true,
        AcceptEULAPrompt = true
      }
);

if (!Client.IsConnected)
{
  DotEnv.LoadFile();
  // Waits until the client has loaded and is ready.
  await client.LogOn(
    username: DotEnv.Get("USERNAME"), // String value
    password: DotEnv.Get("PASSWORD")  // SecureString value
  );
  Console.WriteLine($"Connected as {Client.CurrentUser.Name}.");
}

// Teardown the bot when the MTGO client disconnects.
client.IsConnectedChanged += delegate(object? sender)
{
  Console.WriteLine("The MTGO client has been disconnected. Stopping...");
  client.Dispose();
  Environment.Exit(-1);
};

Console.WriteLine("Finished loading.");
