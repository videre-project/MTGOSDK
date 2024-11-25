/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MTGOSDK.API;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Security;

using MTGOSDK.NUnit.Logging;


namespace MTGOSDK.Tests;

/// <summary>
/// A shared setup fixture that can be used to interact with the global state
/// of the test runner.
/// </summary>
public class Shared : DLRWrapper<Client>
{
  /// <summary>
  /// The default client instance to interact with the MTGO API.
  /// </summary>
  /// <remarks>
  /// This is a shared instance that is intended to be interacted with by all
  /// tests in the test suite in a multi-threaded environment to avoid redundant
  /// setup and teardown operations with the <see cref="SetupFixture"/> class.
  /// </remarks>
#pragma warning disable CA2211 // Non-constant fields should not be visible
  public static Client client = null!;
#pragma warning restore CA2211 // Non-constant fields should not be visible
}

[SetUpFixture]
public class SetupFixture : Shared
{
  [OneTimeSetUp, CancelAfter(/* 5 min */ 300_000)]
  public virtual async Task RunBeforeAnyTests()
  {
    try
    {
      // Skip if the client has already been initialized.
      if (Client.HasStarted && client != null) return;

      client = new Client(
        new ClientOptions
        {
          CreateProcess = true,
          StartMinimized = true,
          // CloseOnExit = true,
          AcceptEULAPrompt = true
        },
        loggerProvider: new NUnitLoggerProvider(LogLevel.Trace)
      );

      // Ensure the MTGO client is not interactive (with an existing user session).
      Assert.That(Client.IsInteractive, Is.False);

      if (!Client.IsConnected)
      {
        DotEnv.LoadFile();
        // Waits until the client has loaded and is ready.
        await client.LogOn(
          username: DotEnv.Get("USERNAME"), // String value
          password: DotEnv.Get("PASSWORD")  // SecureString value
        );
        Assert.That(Client.IsLoggedIn, Is.True);

        // Revalidate the client's reported interactive state.
        Assert.That(Client.IsInteractive, Is.False);
      }

      client.ClearCaches();
    }
    // If an exception occurs, log the error and immediately exit the runner.
    catch (Exception ex)
    {
      // If the exception is an aggregate exception, get the inner exception
      // and unroll the full stack trace.
      string error;
      if (ex is AggregateException ae)
      {
        error = ae.Flatten().ToString();
      }
      else
      {
        error = ex.ToString();
      }

      // Check if inside a GitHub Actions CI environment.
      if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
      {
        TestContext.Error.WriteLine($"::error title=Encountered an error during setup::{error}");
      }
      else
      {
        TestContext.Error.WriteLine(error);
      }

      TestContext.Error.Flush();
      await Task.Delay(1000);
      Environment.Exit(-100);
    }
  }

  [OneTimeTearDown, CancelAfter(/* 10 seconds */ 10_000)]
  public virtual async Task RunAfterAnyTests()
  {
    // Skip if the client has already been disposed.
    if (!RemoteClient.IsInitialized && client == null) return;

    // Log off the client to ensure that the user session terminates.
    if (!Client.IsInteractive)
    {
      await client.LogOff();
      Assert.That(Client.IsLoggedIn, Is.False);
    }

    // Set a callback to indicate when the client has been disposed.
    bool isDisposed = false;
    RemoteClient.Disposed += (s, e) => isDisposed = true;

    // Safely dispose of the client instance.
    client.Dispose();
    client = null!;
    if (!await WaitUntil(() => isDisposed)) // Waits at most 5 seconds.
    {
      Assert.Fail("The client was not disposed within the timeout period.");
    }

    // Verify that all remote handles have been reset.
    Assert.That(RemoteClient.IsInitialized, Is.False);
    Assert.That(RemoteClient.Port, Is.Null);
  }
}
