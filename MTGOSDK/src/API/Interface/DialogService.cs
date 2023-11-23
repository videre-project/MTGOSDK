/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Windows;

using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Reflection;

using Shiny.Core;
using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface;

/// <summary>
/// Manages the client's dialog services.
/// </summary>
public static class DialogService
{
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
    using var viewModel = new GenericDialogViewModel(
      title,
      text,
      okButton,
      cancelButton
    );
    return (bool)s_dialogService.ShowModal<dynamic>(viewModel.@base, -1);
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
    var relatedView = MainRelatedView;
    // if (uri is not null)
    //   RemoteClient.CreateInstance(/* IRelayCommand */, () =>
    //     s_toastController.WindowsShell.StartProcess(uri.OriginalString));

    using var viewModel = new BasicToastViewModel(title, text, relatedView);
    s_toastViewManager.DisplayToast(viewModel.@base);
  }
}
