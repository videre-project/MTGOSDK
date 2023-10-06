/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API;

// using MTGOInjector;
// using ScubaDiver.API.Hooking;


//
// Instantiate a new Client wrapper object attached to the MTGO process.
//
// This will always attach to the latest running MTGO process, or throw an
// exception if no MTGO process is found. Once created, the connection to the
// MTGO process is available to the SDK via a singleton RemoteClient instance.
//
var client = new Client();

//
// Get user properties to dump.
//
// Note that SecureString properties cannot be read or accessed with reflection
// as they are encrypted in memory and inaccessible to ClrMD. This is intended
// to prevent any access to sensitive information stored in the client's memory.
//
var user = client.CurrentUser;

int userId = user.Id;
string username = user.Name;
Console.WriteLine($"User #{userId} logged in as '{username}'");

// //
// // This is a simple example of how to use the client's Toast method to display
// // a simple toast modal on the client.
// //
// client.Toast("MTGO Injector", $"User #{userId} logged in as '{username}'");

// //
// // This is a basic example of how to use the Harmony hooking API provided by
// // ScubaDiver. This example listens for when the MainUI is interacted with.
// //
// client.HookInstanceMethod(MTGOTypes.Get("ShellView"),
//     methodName: "MainViewModel_PropertyChanged",
//     hookName: "prefix", // Can be any of 'prefix', 'postfix' or 'finalizer':
//                         //   - 'prefix' will execute before the method.
//                         //   - 'postfix' will execute after the method.
//                         //   - 'finalizer' wraps the method in a try/finally.
//     callback: new((HookContext context, dynamic instance, dynamic[] args)
//       => {
//         var sender = args[0];
//         var e = args[1];
//         Console.WriteLine("MainViewModel_PropertyChanged");
//         Console.WriteLine($"  Sender: {sender.ToString()}");
//         Console.WriteLine($"  Property: {e.PropertyName}");
//       }));

// For this demo, wait until key press to ensure that hook callbacks can fire.
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
