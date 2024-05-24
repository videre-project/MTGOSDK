/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Logging;

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
  private static readonly IDialogService s_dialogService =
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
    Log.Information("Showing dialog window: {Title} - {Text}", title, text);
    using var viewModel = new GenericDialogViewModel(
      title,
      text,
      okButton,
      cancelButton
    );
    return (bool)s_dialogService.ShowModal<dynamic>(viewModel.@base, -1);
  }
}
