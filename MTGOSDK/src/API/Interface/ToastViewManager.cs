/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Shiny.Core;
using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Interface;
using static MTGOSDK.API.Events;

/// <summary>
/// Manages the client's toast notification services.
/// </summary>
public static class ToastViewManager
{
  //
  // IToastViewManager wrapper methods
  //

  /// <summary>
  /// Manages the client's connection and user session information.
  /// </summary>
  private static readonly ISession s_session =
    ObjectProvider.Get<ISession>();

  /// <summary>
  /// Global manager for creating and displaying toast modal on the client.
  /// </summary>
  private static readonly IToastViewManager s_toastViewManager =
    ObjectProvider.Get<IToastViewManager>();

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
    Log.Information("Showing toast notification: {Title} - {Text}", title, text);
    var relatedView = MainRelatedView;
    // if (uri is not null)
    //   RemoteClient.CreateInstance(/* IRelayCommand */, () =>
    //     s_toastController.WindowsShell.StartProcess(uri.OriginalString));

    using var viewModel = new BasicToastViewModel(title, text, relatedView);
    Unbind(s_toastViewManager).DisplayToast(Unbind(viewModel));
  }

  //
  // IToastViewManager wrapper events
  //

  public static EventProxy<ToastEventArgs> ToastRequested =
    new(/* ISession */ s_session, nameof(ToastRequested));
}
