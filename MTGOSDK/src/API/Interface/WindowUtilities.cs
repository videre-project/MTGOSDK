/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using ICollection = System.Collections.Generic.ICollection<dynamic>;
using System.Windows;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface;
using static MTGOSDK.Core.Reflection.DLRWrapper;

/// <summary>
/// Manages the client's WPF Windows and window utilities
/// </summary>
public static class WindowUtilities
{
  //
  // IWindowUtilities wrapper methods
  //

  /// <summary>
  /// Shared utilities class for manipulating WPF Window objects.
  /// </summary>
  private static readonly IWindowUtilities s_windowUtilities =
    ObjectProvider.Get<IWindowUtilities>();

  /// <summary>
  /// Gets a collection of the client's open windows.
  /// </summary>
  /// <returns>A collection of Window objects</returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown if the window collection can not be retrieved.
  /// </exception>
  public static ICollection GetWindows()
  {
    // Attempt to retrieve the updated window collection from client memory.
    Log.Trace("Getting window collection from client memory.");
    dynamic collection = Retry(delegate
    {
      return RemoteClient.GetInstances(new TypeProxy<WindowCollection>())
        .LastOrDefault() ?? throw null;
    }) ?? new InvalidOperationException("Failed to get window collection.");

    return Bind<ICollection<dynamic>>(collection);
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
    if (Client.IsConnected && Client.IsInteractive)
      throw new InvalidOperationException("Cannot close dialogs in an interactive session.");

    Log.Information("Closing all dialog windows.");
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
}
