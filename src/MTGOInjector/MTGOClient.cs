/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
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
      HookInstanceMethod(MTGOTypes.Get("App"), "OnExit",
          hookName: "finalizer",
          callback: new((HookContext context, dynamic instance, dynamic[] args)
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

  /// <summary>
  /// The Local AppData directory for storing MTGOInjector assemblies.
  /// </summary>
  protected override string ExtractDir =>
    Path.Join(/* %appdata%\..\Local\ */ "MTGOInjector", "bin");

  //
  // MTGO class instances
  //

  /// <summary>
  /// The MTGO client's main application entrypoint.
  /// </summary>
  public dynamic App => GetInstance(MTGOTypes.Get("App"));

  /// <summary>
  /// The MTGO client's modal dialog service.
  /// </summary>
  public dynamic DialogService => GetInstance(MTGOTypes.Get("DialogService"));

  /// <summary>
  /// The MTGO client's toast notification service.
  /// </summary>
  public dynamic ToastController => GetInstance(MTGOTypes.Get("ToastViewManager"));

  //
  // Derived properties
  //

  /// <summary>
  /// Returns the path to the MTGO client's assembly directory.
  /// </summary>
  public string AssemblyPath =>
    InvokeMethod(MTGOTypes.Get("Utility"), "GetAssemblyPath");

  /// <summary>
  /// Returns the path to the MTGO client's data directory.
  /// </summary>
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
    //
    // This calls the `Log.EndSession` method on the Gibraltar.Agent class,
    // which can be called multiple times safely.
    //
    // Refer to the 'Ending a Session' section of the Gibraltar docs:
    // https://doc.onloupe.com/Gibraltar.Agent~Gibraltar.Agent.Log.html
    //
    App.EndGibraltarSession("Normal Shutdown");
  }

  //
  // MTGO interactive methods
  //

  /// <summary>
  /// Displays a dialog window on the MTGO client with the given title and text.
  /// </summary>
  /// <returns>A boolean representing the user response</returns>
  public bool DialogWindow(
    string title,
    string text,
    string? okButton="Ok",
    string? cancelButton="Cancel")
  {
    dynamic viewModel = CreateInstance(MTGOTypes.Get("GenericDialogViewModel"));
    viewModel.m_title = title;
    viewModel.m_text = text;
    if (viewModel.m_showOkButton = okButton != null)
      viewModel.m_okayButtonLabel = okButton;
    if (viewModel.m_showCancelButton = cancelButton != null)
      viewModel.m_cancelButtonLabel = cancelButton;

    bool result = DialogService.ShowModal<dynamic>(viewModel, -1);
    DialogService.TryDisposeViewModel(viewModel);

    return result;
  }

  public void Toast(string title, string text, Uri? uri=null)
  {
    var relatedView = ObjectProvider("ShellViewModel").MainRelatedView;
    // if (uri is not null)
    //   CreateInstance(MTGOTypes.Get("RelayCommand"), () =>
    //     ToastController.WindowsShell.StartProcess(uri.OriginalString));

    dynamic toastViewModel = CreateInstance(MTGOTypes.Get("BasicToastViewModel"),
      text, relatedView, title, false);

    ToastController.DisplayToast(toastViewModel);
  }
}
