/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Windows;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API;

public static class DialogService
{
  //
  // IDialogService wrapper methods
  //

  // public static IDialogService s_dialogService =>
  //   ObjectProvider.Get<IDialogService>();

  //
  // IWindowUtilities wrapper methods
  //

  private static IWindowUtilities s_windowUtilities =>
    ObjectProvider.Get<IWindowUtilities>();

  /// <summary>
  /// Gets a collection of the client's open windows.
  /// </summary>
  /// <returns>A collection of Window objects</returns>
  public static ICollection<dynamic> GetWindows()
  {
    // This is a hack that caches the dispatcher's registered windows.
    _ = DLRWrapper<dynamic>.Unbind(s_windowUtilities).AllWindows;

    var collection = RemoteClient
      .GetInstances(new Proxy<WindowCollection>())
      .LastOrDefault()
        ?? throw new Exception("Window collection not initialized.");

    return DLRWrapper<dynamic>.Bind<ICollection<dynamic>>(collection);
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
      try { window.m_closable.DialogResult = true; } catch { /* not a dialog */ }
    }
  }
}
