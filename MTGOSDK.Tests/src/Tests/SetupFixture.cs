/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NUnit.Framework.Internal;

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
[RetryOnError(1, RetryBehavior.UntilPasses)]
public class Shared : DLRWrapper<Client>
{
  public DateTime StartTime;

  public DateTime EndTime;

  public TimeSpan Duration => EndTime - StartTime;

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
  private static readonly NUnitLoggerProvider s_loggerProvider =
    new(LogLevel.Trace)
    {
      FileLoggerStreamWriter = new StreamWriter(
        Path.Combine(
          Environment.CurrentDirectory,
          $".testresults-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log"),
        append: true)
    };

  private static readonly NUnitLogger s_logger =
    (NUnitLogger)s_loggerProvider.CreateLogger(nameof(SetupFixture));

  private static void Write(string message)
  {
    string formattedMessage = $"----------------------- {message}";
    NUnitLogger.Write(formattedMessage);
    s_logger.Log(formattedMessage);
  }

  [OneTimeSetUp, CancelAfter(/* 5 min */ 300_000)]
  public virtual async Task RunBeforeAnyTests()
  {
    Write($"{nameof(SetupFixture)}.{nameof(RunBeforeAnyTests)}");
    {
      // Disable NUnit's console redirection during setup.
      NUnitLogger.UseImmediateFlush = true;

      // Set the start time for the test fixture.
      StartTime = DateTime.Now;
    }

    try
    {
      // Skip if the client has already been initialized.
      if (Client.HasStarted && client != null) return;

      client = new Client(
        new ClientOptions
        {
          CreateProcess = true,
          StartMinimized = true,
          AcceptEULAPrompt = true
        },
        loggerProvider: s_loggerProvider
      );

      // Ensure the MTGO client is not interactive (with an existing user session).
      Assert.That(client.IsInteractive, Is.False);

      if (!client.IsConnected)
      {
        DotEnv.LoadFile();
        // Waits until the client has loaded and is ready.
        await client.LogOn(
          username: DotEnv.Get("USERNAME"), // String value
          password: DotEnv.Get("PASSWORD")  // SecureString value
        );
        Assert.That(await Client.IsOnline(), Is.True);
        Assert.That(client.IsLoggedIn, Is.True);

        // Revalidate the client's reported interactive state.
        Assert.That(client.IsInteractive, Is.False);
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
    finally
    {
      EndTime = DateTime.Now;
      Write($"Took {Duration.TotalSeconds:F2} seconds\n");

      // Use NUnit's console redirection after setup has completed.
      NUnitLogger.UseImmediateFlush = false;
    }
  }

  [OneTimeTearDown, CancelAfter(/* 30 seconds */ 30_000)]
  public virtual async Task RunAfterAnyTests()
  {
    Write($"{nameof(SetupFixture)}.{nameof(RunAfterAnyTests)}");
    {
      // Disable NUnit's console redirection during teardown.
      NUnitLogger.UseImmediateFlush = true;

      // Set the start time for the test fixture.
      StartTime = DateTime.Now;
    }

    try
    {
      // Skip if the client has already been disposed.
      if (!RemoteClient.IsInitialized && client == null) return;

      // Log off the client to ensure that the user session terminates.
      bool isLoggedIn = true;
      if (!client.IsInteractive)
      {
        await client.LogOff();
        Assert.That(client.IsLoggedIn, Is.False);
        isLoggedIn = false;
      }

      // Safely teardown the client instance and wait for disposal.
      client.Dispose();
      client = null!;
      await RemoteClient.WaitForDisposeAsync();
      Assert.That(RemoteClient.IsDisposed, Is.True);

      // Verify that all remote handles have been reset.
      Assert.That(RemoteClient.IsInitialized, Is.False);
      Assert.That(RemoteClient.Port, Is.Null);

      // Finally, kill the process to ensure that all resources are released.
      if (!isLoggedIn)
      {
        RemoteClient.KillProcess();
        Assert.That(RemoteClient.HasStarted, Is.False);
      }
    }
    finally
    {
      EndTime = DateTime.Now;
      Write($"Took {Duration.TotalSeconds:F2} seconds");

      // Use NUnit's console redirection after setup has completed.
      NUnitLogger.UseImmediateFlush = false;
    }
  }
}
