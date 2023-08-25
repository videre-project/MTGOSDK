/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/

using System;
using System.Threading.Tasks;

using MTGOInjector;


//
// Instantiate a new MTGOClient wrapper object attached to the MTGO process.
//
// This will always attach to the latest running MTGO process, or throw an
// exception if no MTGO process is found.
//
var client = new MTGOClient();

//
// Get a remote FlsClientSession object.
//
// The ObjectProvider method returns the singleton object registered when the
// client is bootstrapped. This wraps the same method used internally to access
// these singleton objects and is provided for convenience to avoid having to
// manually resolve the object from the client's memory.
//
// This FlsClientSession object contains basic information about the current 
// user and client session.
//
dynamic FlsClientSession = client.ObjectProvider("FlsClientSession");

//
// Get user properties to dump.
//
// Note that SecureString properties cannot be read or accessed with reflection
// as they are encrypted in memory and inaccessible to ClrMD. This is intended
// to prevent any access to sensitive information stored in the client's memory.
//
int userId = FlsClientSession.m_loggedInUser.Id;
string username = FlsClientSession.m_loggedInUser.Name;
Console.WriteLine($"User #{userId} logged in as '{username}'");

//
// This is a simple example of how to use the client's DialogWindow method to
// display a message box modal on the client.
//
client.DialogWindow("MTGO Injector",
                    $"User #{userId} logged in as '{username}'",
                    cancelButton: null);


// For this demo, wait indefinitely to ensure any invoked async methods fire.
while (true)
{
  await Task.Delay(5000);
}
