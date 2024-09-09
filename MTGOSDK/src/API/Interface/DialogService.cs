/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Interface.Windows;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Compiler;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface;

/// <summary>
/// Manages the client's dialog services.
/// </summary>
public static class DialogService
{
  /// <summary>
  /// Global manager for creating and displaying dialog windows on the client.
  /// </summary>
  private static readonly IDialogService s_dialogService =
    ObjectProvider.Get<IDialogService>();

  /// <summary>
  /// Maps a collection of windows to a list of GenericWindow objects.
  /// </summary>
  private static readonly Func<dynamic, IList<GenericWindow>> s_stackMapper =
    (windows) => Map<IList, GenericWindow>(windows, proxy: true);

  public static DictionaryProxy<int, dynamic> foo =
    new(Unbind(s_dialogService).m_windowsForIds, valueMapper: s_fooMapper);

  private static readonly Func<dynamic, dynamic> s_fooMapper =
    (windows) => windows;

  /// <summary>
  /// The client's open windows, indexed by their window ID.
  /// </summary>
  /// <remarks>
  /// This dictionary contains the client's open windows, indexed by a unique
  /// window ID starting at 0. By default, the client's main window is indexed
  /// at 0, with subsequent windows indexed at 1, 2, 3, etc.
  /// </remarks>
  public static DictionaryProxy<int, IList<GenericWindow>> RegisteredWindows =
    new(Unbind(s_dialogService).m_windowsForIds, valueMapper: s_stackMapper);

  /// <summary>
  /// Get the current active window on the client.
  /// </summary>
  public static GenericWindow ActiveWindow =>
    new(Unbind(s_dialogService).GetActiveWindow());

  //
  // Modal dialog methods
  //

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
    Log.Trace("Showing dialog window: {Title} - {Text}", title, text);
    return ShowModal<GenericDialogViewModel>(title, text, okButton, cancelButton);
  }

  /// <summary>
  /// Displays a dialog window on the MTGO client with the given title and text.
  /// </summary>
  /// <typeparam name="T">The type of dialog viewmodel to display.</typeparam>
  /// <param name="title">The title of the dialog window.</param>
  /// <param name="text">The text to display in the dialog window.</param>
  /// <param name="okButton">The text to display on the OK button (optional).</param>
  /// <param name="cancelButton">The text to display on the Cancel button (optional).</param>
  /// <returns>True if the OK button was clicked, otherwise false.</returns>
  public static bool ShowModal<T>(params object[] args)
      where T : DLRWrapper<IBasicDialogViewModelBase>, IDisposable
  {
    using var viewModel = InstanceFactory.CreateInstance(typeof(T), args) as T;
    return ShowModal(viewModel);
  }

  /// <summary>
  /// Displays a dialog window on the MTGO client with the given viewmodel.
  /// </summary>
  /// <param name="viewModel">The viewmodel to display in the dialog window.</param>
  /// <returns>True if the OK button was clicked, otherwise false.</returns>
  public static bool ShowModal(DLRWrapper viewModel)
  {
    return (bool)s_dialogService.ShowModal<dynamic>(viewModel.@base, -1);
  }

  //
  // File dialog methods
  //

  /// <summary>
  /// Displays an open file dialog and returns the selected file paths.
  /// </summary>
  /// <param name="defaultExt">The default file extension.</param>
  /// <param name="filter">The file filter string.</param>
  /// <param name="title">The title of the dialog.</param>
  /// <param name="multiselect">Whether multiple files can be selected.</param>
  /// <returns>An array of the selected file paths.</returns>
  public static string[] ShowOpenFileDialog(
    string defaultExt,
    string filter,
    string title,
    bool multiselect = false)
  {
    return s_dialogService.ShowOpenFileDialog(defaultExt, filter, title, multiselect);
  }

  /// <summary>
  /// Displays a save file dialog and returns the selected file path.
  /// </summary>
  /// <param name="defaultExt">The default file extension.</param>
  /// <param name="filter">The file filter string.</param>
  /// <param name="title">The title of the dialog.</param>
  /// <param name="fileName">The initial file name.</param>
  /// <returns>The selected file path.</returns>
  public static string ShowSaveFileDialog(
    string defaultExt,
    string filter,
    string title,
    string fileName)
  {
    return s_dialogService.ShowSaveFileDialog(defaultExt, filter, title, fileName);
  }

  public static string ShowFolderBrowserDialog(
    Environment.SpecialFolder folder = Environment.SpecialFolder.MyComputer,
    string title = "Select a folder")
  {
    return s_dialogService.ShowFolderBrowserDialog(folder, title);
  }
}
