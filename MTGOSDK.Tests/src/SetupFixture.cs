/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MTGOSDK.API;
using MTGOSDK.Core.Security;

using MTGOSDK.NUnit.Logging;


namespace MTGOSDK.Tests;

[SetUpFixture]
public class SetupFixture
{
  /// <summary>
  /// A shared setup fixture that can be used to interact with the global state
  /// of the test runner.
  /// </summary>
  public class Shared : SetupFixture
  {
#pragma warning disable CS1998
  public override async Task RunBeforeAnyTests() { }
  public override void RunAfterAnyTests() { }
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
    client = new Client(
      Client.HasStarted
        ? new ClientOptions()
        : new ClientOptions
          {
            CreateProcess = true,
            // DestroyOnExit = true,
            AcceptEULAPrompt = true
          },
      // loggerProvider: new NUnitLoggerProvider(LogLevel.Debug)
      loggerProvider: new NUnitLoggerProvider()
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
  public virtual void RunAfterAnyTests()
  {
    client.Dispose();
  }
}
