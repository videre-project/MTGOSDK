/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/

using MTGOInjector;


var client = new MTGOClient();

// Get a remote FlsClientSession object
dynamic FlsClient = client.ObjectProvider("FlsClientSession");

// Get sensitive properties to dump
int userId = FlsClient.m_loggedInUser.Id;
string username = FlsClient.m_loggedInUser.Name;
Console.WriteLine($"User #{userId} logged in as '{username}'");

client.DialogWindow("MTGO Injector",
                    $"User #{userId} logged in as '{username}'",
                    cancelButton: null);


while (true)
{
  await Task.Delay(5000);
}
