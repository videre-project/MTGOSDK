/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Windows;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

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
  private static IWindowUtilities s_windowUtilities =
    ObjectProvider.Get<IWindowUtilities>();

  /// <summary>
  /// Gets a collection of the client's open windows.
  /// </summary>
  /// <returns>A collection of Window objects</returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown if the window collection can not be retrieved.
  /// </exception>
  public static ICollection<dynamic> GetWindows()
  {
#if NETSTANDARD2_0
    throw new PlatformNotSupportedException();
#else
    // This is a hack that caches the dispatcher's registered windows.
    _ = Unbind(s_windowUtilities).AllWindows;
    _ = s_windowUtilities.AllWindows;

    // Attempt to retrieve the updated window collection from client memory.
    for (var retries = 5; retries > 0; retries--)
    {
      try
      {
        var collection = RemoteClient
          .GetInstances(new Proxy<WindowCollection>())
          .LastOrDefault() ?? throw null;

        return Bind<ICollection<dynamic>>(collection);
      }
      catch { }
    }

    throw new InvalidOperationException("Failed to get window collection.");
#endif
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
}
