/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Play;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Shiny.Core;
using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Interface;
using static MTGOSDK.API.Events;

/// <summary>
/// Manages the client's toast notification services.
/// </summary>
public static class NotificationService
{
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

  //
  // IToastViewManager wrapper methods
  //

  /// <summary>
  /// Displays a toast notification on the MTGO client with the given title and text.
  /// </summary>
  /// <param name="title">The title of the toast notification.</param>
  /// <param name="text">The text to display in the toast notification.</param>
  /// <param name="playerEvent">The event to associate with the toast notification (Optional).</param>
  /// <param name="persistent">True if the toast notification should persist until dismissed, otherwise false.</param>
  public static void ShowToast(
    string title,
    string text,
    Event? playerEvent = null,
    bool persistent = false)
  {
    using var viewModel = playerEvent?.@base is IPlayerEvent
      ? new BasicToastViewModel(title, text, playerEvent, persistent)
      : new BasicToastViewModel(title, text, MainRelatedView, persistent);

    Log.Trace("Showing toast notification: {Title} - {Text}", title, text);
    ShowToast(viewModel);
  }

  /// <summary>
  /// Displays a dialog window on the MTGO client with the given viewmodel.
  /// </summary>
  /// <param name="viewModel">The viewmodel to display in the dialog window.</param>
  /// <returns>True if the OK button was clicked, otherwise false.</returns>
  public static void ShowToast(DLRWrapper viewModel)
  {
    Unbind(s_toastViewManager).DisplayToast(Unbind(viewModel));
  }

  //
  // IToastViewManager wrapper events
  //

  /// <summary>
  /// Occurs when the MTGO client displays a toast notification.
  /// </summary>
  public static EventProxy<ToastEventArgs> ToastRequested =
    new(/* ISession */ s_session, nameof(ToastRequested));
}
