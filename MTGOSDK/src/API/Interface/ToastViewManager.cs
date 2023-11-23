/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Reflection;

using Shiny.Core;
using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface;

/// <summary>
/// Manages the client's toast notification services.
/// </summary>
public static class ToastViewManager
{
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
