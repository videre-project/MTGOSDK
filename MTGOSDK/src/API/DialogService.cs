/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Windows;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using Shiny.Core;
using Shiny.Core.Interfaces;


namespace MTGOSDK.API;

/// <summary>
/// Manages the client's window utilities and dialog services.
/// </summary>
public static class DialogService
{
  //
  // IWindowUtilities wrapper methods
  //

  /// <summary>
  /// Shared utilities class for manipulating WPF Window objects.
  /// </summary>
  private static IWindowUtilities s_windowUtilities =
    ObjectProvider.Get<IWindowUtilities>();

  /// <summary>
  /// Gets a collection of the client's open windows.
  /// </summary>
  /// <returns>A collection of Window objects</returns>
  public static ICollection<dynamic> GetWindows()
  {
    // This is a hack that caches the dispatcher's registered windows.
    _ = DLRWrapper<dynamic>.Unbind(s_windowUtilities).AllWindows;
    _ = s_windowUtilities.AllWindows;

    // Attempt to retrieve the updated window collection from client memory.
    for (var retries = 5; retries > 0; retries--)
    {
      try
      {
        var collection = RemoteClient
          .GetInstances(new Proxy<WindowCollection>())
          .LastOrDefault()
            ?? throw new Exception("Window collection not initialized.");

        return DLRWrapper<dynamic>.Bind<ICollection<dynamic>>(collection);
      }
      catch { }
    }

    throw new Exception("Failed to get window collection.");
  }

  /// <summary>
  /// Closes all open dialog windows, unblocking the client's MainUI thread.
  /// </summary>
  /// <remarks>
  /// This will close the window if it is a dialog window, returning true for
  /// any waiting Window.ShowDialog() calls.
  /// </remarks>
  public static void CloseDialogs()
  {
    foreach(var window in GetWindows())
    {
      //
      // Sets the DialogResult property of the IClosableViewModel proxy object,
      // which is bound to the base window's DialogResult property.
      //
      if (window.GetType().Name == "BaseDialog" && !window.m_isWindowClosing)
      {
        // Setting the DialogResult property value will also close the window.
        try { window.m_closable.DialogResult = true; } catch { /* Closed */ }
      }
    }
  }

  //
  // IDialogService wrapper methods
  //

  /// <summary>
  /// Global manager for creating and displaying dialog windows on the client.
  /// </summary>
  private static IDialogService s_dialogService =
    ObjectProvider.Get<IDialogService>();

  /// <summary>
  /// Displays a dialog window on the MTGO client with the given title and text.
  /// </summary>
  /// <param name="title">The title of the dialog window.</param>
  /// <param name="text">The text to display in the dialog window.</param>
  /// <param name="okButton">The text to display on the OK button (optional).</param>
  /// <param name="cancelButton">The text to display on the Cancel button (optional).</param>
  /// <returns>True if the OK button was clicked, otherwise false.</returns>
  public static bool ShowModal(
    string title,
    string text,
    string? okButton="Ok",
    string? cancelButton="Cancel")
  {
    var genericDialogViewModel = "Shiny.ViewModels.GenericDialogViewModel";
    var viewModel = RemoteClient.CreateInstance(genericDialogViewModel);

    viewModel.m_title = title;
    viewModel.m_text = text;
    if (viewModel.m_showOkButton = okButton != null)
      viewModel.m_okayButtonLabel = okButton;
    if (viewModel.m_showCancelButton = cancelButton != null)
      viewModel.m_cancelButtonLabel = cancelButton;

    bool result = s_dialogService.ShowModal<dynamic>(viewModel, -1);
    viewModel.Dispose();

    return result;
  }

  //
  // IToastViewManager wrapper methods
  //

  /// <summary>
  /// Global manager for creating and displaying toast modal on the client.
  /// </summary>
  private static dynamic s_toastViewManager =
    ObjectProvider.Get<IToastViewManager>(bindTypes: false);

  /// <summary>
  /// The main shell view currently displayed on the primary MTGO window.
  /// </summary>
  private static IToastRelatedView MainRelatedView =>
    ObjectProvider.Get<IShellViewModel>().MainRelatedView;

  /// <summary>
  /// Displays a toast notification on the MTGO client with the given title and text.
  /// </summary>
  /// <param name="title">The title of the toast notification.</param>
  /// <param name="text">The text to display in the toast notification.</param>
  /// <param name="uri">The URI to open when the toast notification is clicked (optional).</param>
  public static void ShowToast(string title, string text, Uri? uri=null)
  {
    var relatedView = DLRWrapper<dynamic>.Unbind(MainRelatedView);
    // if (uri is not null)
    //   RemoteClient.CreateInstance(/* RelayCommand */, () =>
    //     s_toastController.WindowsShell.StartProcess(uri.OriginalString));

    var basicToastViewModel = "Shiny.Toast.ViewModels.BasicToastViewModel";
    dynamic toastViewModel = RemoteClient.CreateInstance(basicToastViewModel,
      text, relatedView, title, false);

    s_toastViewManager.DisplayToast(toastViewModel);
    toastViewModel.Dispose();
  }
}
