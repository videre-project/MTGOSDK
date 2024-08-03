/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MTGOSDK.API;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Security;

using MTGOSDK.NUnit.Logging;


namespace MTGOSDK.Tests;

[SetUpFixture]
public class SetupFixture : DLRWrapper<Client>
{
  /// <summary>
  /// A shared setup fixture that can be used to interact with the global state
  /// of the test runner.
  /// </summary>
  public class Shared : SetupFixture
  {
#pragma warning disable CS1998
  public override async Task RunBeforeAnyTests() { }
  public override async Task RunAfterAnyTests() { }
#pragma warning restore CS1998
  }

  /// <summary>
  /// The default client instance to interact with the MTGO API.
  /// </summary>
  /// <remarks>
  /// This is a shared instance that is intended to be interacted with by all
  /// tests in the test suite in a multi-threaded environment to avoid redundant
  /// setup and teardown operations with the <see cref="SetupFixture"/> class.
  /// </remarks>
  public static Client client { get; private set; } = null!;

  [OneTimeSetUp]
  public virtual async Task RunBeforeAnyTests()
  {
    // Skip if the client has already been initialized.
    if (Client.HasStarted && client != null) return;

    client = new Client(
      Client.HasStarted
        ? new ClientOptions()
        : new ClientOptions
          {
            CreateProcess = true,
            StartMinimized = true,
            // DestroyOnExit = true,
            AcceptEULAPrompt = true
          },
      loggerProvider: new NUnitLoggerProvider(LogLevel.Trace)
    );

    if (!Client.IsConnected)
    {
      DotEnv.LoadFile();
      // Waits until the client has loaded and is ready.
      await client.LogOn(
        username: DotEnv.Get("USERNAME"), // String value
        password: DotEnv.Get("PASSWORD")  // SecureString value
      );
    }

    client.ClearCaches();
  }

  [OneTimeTearDown]
  public virtual async Task RunAfterAnyTests()
  {
    // Skip if the client has already been disposed.
    if (!RemoteClient.IsInitialized && client == null) return;

    // Set a callback to indicate when the client has been disposed.
    bool isDisposed = false;
    RemoteClient.Disposed += (s, e) => isDisposed = true;

    // Safely dispose of the client instance.
    client.Dispose();
    client = null!;
    await WaitUntil(() => isDisposed);

    // Verify that all remote handles have been reset.
    Assert.That(RemoteClient.IsInitialized, Is.False);
    Assert.That(RemoteClient.Port, Is.Null);
  }
}
