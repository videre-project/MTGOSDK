/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/

using System.Diagnostics;

using ScubaDiver.API.Hooking;


namespace MTGOInjector;

public class MTGOClient : BaseClient
{
  public MTGOClient()
  {
    // Only run these hooks on the initial injection
    if (Is_Reconnect == false)
    {
      // Disable the Gibraltar logging session
      DisableTelemetry();

      // Hook into the `Shiny.App.OnExit` method to tear down the diver
      HookInstanceMethod(MTGOTypes.Get("App"), "OnExit", "prefix",
          new((HookContext context, dynamic instance, dynamic[] args)
            => Dispose()));
    }
  }

  //
  // BaseClient properties
  //

  /// <summary>
  /// The MTGO client process.
  /// </summary>
  protected override Process ClientProcess =>
    Process.GetProcessesByName("MTGO")
      .OrderBy(x => x.StartTime)
      .FirstOrDefault()
        ?? throw new Exception("MTGO process not found.");

  //
  // MTGO class instances
  //

  /// <summary>
  /// <para>Type: System.Appdomain</para>
  /// The logical memory boundary for the MTGO client.
  /// </summary>
  public dynamic AppDomain => GetInstance("System.AppDomain");

  /// <summary>
  /// The MTGO client's main application entrypoint.
  /// </summary>
  public dynamic App => GetInstance(MTGOTypes.Get("App"));

  //
  // Derived properties
  //

  public string AssemblyPath =>
    InvokeMethod(MTGOTypes.Get("Utility"), "GetAssemblyPath");

  public string DataRootPath =>
    InvokeMethod(MTGOTypes.Get("Utility"), "GetDataRootPath");

  /// <summary>
  /// The current MTGO client's Gibraltar session id.
  /// </summary>
  public string SessionId => App.m_sessionId;

  //
  // MTGOClient methods
  //

  /// <summary>
  /// Calls the <c>Get</c> method on the ObjectProvider static class.
  /// <para> Useful for retrieving singleton instances of MTGO classes.</para>
  /// </summary>
  public dynamic ObjectProvider(string className)
  {
    string interfaceName = MTGOTypes.Get(className, key: "Interface")
      ?? throw new ArgumentException($"Interface not found for {className}.");

    Type genericType = GetInstanceType(interfaceName);
    return InvokeMethod(MTGOTypes.Get("ObjectProvider"),
                        methodName: "Get", // ObjectProvider.Get<T>()
                        genericTypes: new Type[] { genericType });
  }

  /// <summary>
  /// Disables the Gibraltar telemetry session that is started by the client.
  /// </summary>
  public void DisableTelemetry()
  {
    // Disable the Gibraltar logging session
    App.EndGibraltarSession("Normal Shutdown");

    // Disable subsequent calls to prevent invalid requests
    HookInstanceMethod(MTGOTypes.Get("App"), "EndGibraltarSession",
      hookName: "prefix",
      callback: new((HookContext context, dynamic instance, dynamic[] args)
        => context.CallOriginal = false));
  }
}
