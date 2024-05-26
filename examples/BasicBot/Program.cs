/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using MTGOSDK.API;
using MTGOSDK.Core.Logging;
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

ILoggerFactory factory = LoggerFactory.Create(builder =>
{
  builder.AddConsole();
  builder.SetMinimumLevel(LogLevel.Debug);
  // Add additional logging configuration here.
});

// Initialize the client instance.
bool isAlreadyRunning = !restart && Client.HasStarted;
using var client = new Client(
  isAlreadyRunning
    ? new ClientOptions()
    : new ClientOptions
      {
        CreateProcess = true,
        DestroyOnExit = true,
        AcceptEULAPrompt = true
      },
  loggerFactory: factory
);
Log.Information("Connected to MTGO v{Version}.", Client.Version);

if (!Client.IsConnected)
{
  DotEnv.LoadFile();
  // Waits until the client has loaded and is ready.
  await client.LogOn(
    username: DotEnv.Get("USERNAME"), // String value
    password: DotEnv.Get("PASSWORD")  // SecureString value
  );
  Log.Information("Connected as {Username}.", Client.CurrentUser.Name);
}

// Teardown the bot when the MTGO client disconnects.
client.IsConnectedChanged += delegate(object? sender)
{
  Log.Information("The MTGO client has been disconnected.");
  // Optional teardown logic here.
};

Log.Information("Finished loading.");

if (!isAlreadyRunning)
{
  await client.LogOff();
  Log.Information("Stopped the bot.");
}
