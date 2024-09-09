/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Windows;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface;

// TODO: Add Shiny.Core.WindowExtensions

/// <summary>
/// Manages the client's WPF Windows and window utilities
/// </summary>
public static class WindowUtilities
{
  /// <summary>
  /// Shared utilities class for manipulating WPF Window objects.
  /// </summary>
  private static readonly IWindowUtilities s_windowUtilities =
    ObjectProvider.Get<IWindowUtilities>();

  public static IEnumerable<dynamic> AllWindows =>
    RemoteClient.GetInstances(new TypeProxy<Window>());

  // internal static GenericWindow GetDispatcherAccess(dynamic genericWindow) =>
  //   GetWindowCollections()
  //     .SelectMany<dynamic, GenericWindow>((o) => Map<GenericWindow>(o))
  //     .FirstOrDefault((o) =>
  //         Try(() => Unbind(o).GetHashCode() == genericWindow.GetHashCode()));

    // RemoteClient
    //   .GetInstances(new TypeProxy<WindowCollection>())
    //   // Cast the returned instances as a nested collection of windows.
    //   .Select((o) => Map<dynamic>(o))
    //   .SelectMany<dynamic, GenericWindow>((o) => Map<GenericWindow>(o))
    //   // Filter out windows that are closing or have a close signal.
    //   .Where((o) => !Unbind(o).m_IsWindowClosing &&
    //                 !Unbind(o).m_closable.CloseSignal)
    //   // Deduplicate based on window hash code
    //   .Distinct(EqualityComparer<GenericWindow>.Default);

    // Optional<GenericWindow>(new Func<dynamic>(() =>
    //   GetWindows()
    //     .Where(o => Try(() => o.GetHashCode == genericWindow.GetHashCode()))
    // ));

  //
  // IWindowUtilities wrapper methods
  //

  /// <summary>
  /// Gets a collection of the client's open windows.
  /// </summary>
  /// <returns>A collection of Window objects</returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown if the window collection can not be retrieved.
  /// </exception>
  public static IEnumerable<dynamic> GetWindowCollections()
  {
    // This is a hack that caches the dispatcher's registered windows.
    _ = Unbind(s_windowUtilities).AllWindows;
    _ = s_windowUtilities.AllWindows;

    // Attempt to retrieve the updated window collection from client memory.
    Log.Trace("Getting window collection from client memory.");
    for (var retries = 5; retries > 0; retries--)
    {
      try
      {
        return RemoteClient
          .GetInstances(new TypeProxy<WindowCollection>())
          .Select((o) => Map<dynamic>(o));
      }
      catch
      {
        // Perform exponential backoff
        Task.Delay(100 * (5 - retries)).Wait();
      }
    }

    throw new InvalidOperationException("Failed to get window collection.");
  }

  /// <summary>
  /// Gets a collection of the client's open windows.
  /// </summary>
  /// <returns>A collection of Window objects</returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown if the window collection can not be retrieved.
  /// </exception>
  public static ICollection<dynamic> GetWindows()
  {
    // This is a hack that caches the dispatcher's registered windows.
    _ = Unbind(s_windowUtilities).AllWindows;
    _ = s_windowUtilities.AllWindows;

    // Attempt to retrieve the updated window collection from client memory.
    Log.Trace("Getting window collection from client memory.");
    for (var retries = 5; retries > 0; retries--)
    {
      try
      {
        var collection = RemoteClient
          .GetInstances(new TypeProxy<WindowCollection>())
          .LastOrDefault() ?? throw null;

        return Bind<ICollection<dynamic>>(collection);
      }
      catch
      {
        // Perform exponential backoff
        Task.Delay(100 * (5 - retries)).Wait();
      }
    }

    throw new InvalidOperationException("Failed to get window collection.");
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
