/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOInjector;

public static class MTGOTypes
{
  /// <summary>
  /// Named tuple of class, base, and interface names.
  /// </summary>
  public static Tuple<string, string?, string?> TypeProps(
    string Class,
    string? Base=null,
    string? Interface=null)
  {
    return new (Class, Base, Interface);
  }

  /// <summary>
  /// Map of MTGO class names to their respective assembly paths and properties.
  /// </summary>
  public static Dictionary<string, Tuple<string, string?, string?>> Map =>
    new()
    {
      //
      // Core.dll
      //
      {"DialogService",
        // Manager for creating and displaying dialog windows on the client.
        TypeProps( Class:     "Shiny.Core.DialogManagement.DialogService",
                   Interface: "Shiny.Core.Interfaces.IDialogService" )},
      {"RelayCommand",
        // A queueable implementation of the ICommand interface.
        TypeProps ( Class:    "Shiny.Core.RelayCommand",
                    Base:     "System.Windows.Input.ICommand" )},
      {"GenericDialogViewModel",
        // A generic view model for displaying a message box on the client.
        TypeProps( Class:     "Shiny.ViewModels.GenericDialogViewModel",
                   Base:      "Shiny.ViewModels.BasicDialogViewModelBase" )},
      //
      // FlsClient.dll
      //
      {"FlsClientSession",
        // Provides basic information about the current user and client session.
        TypeProps( Class:     "FlsClient.FlsClientSession",
                   Base:      "FlsClient.ClientSessionBase",
                   Interface: "FlsClient.Interface.IFlsClientSession" )},
      //
      // MTGO.exe
      //
      {"App",
        // The MTGO client's main WPF application entrypoint.
        TypeProps( Class:     "Shiny.App",
                   Base:      "System.Windows.Application" )},
      {"ShellView",
        // Main controller for all client windows and scenes / view models.
        TypeProps( Class:     "Shiny.ShellView",
                   Base:      "Shiny.Chat.Views.ChatWindowHost" )},
      {"ShellViewModel",
        // MainUI view model for the client's main window and scenes.
        TypeProps( Class:     "Shiny.ShellViewModel",
                   Base:      "Shiny.Core.ViewModelBase",
                   Interface: "Shiny.Core.Interfaces.IShellViewModel" )},
      {"ToastController",
        // Main controller for all toast modals on the client.
        TypeProps ( Class:    "Shiny.Toast.ToastController" )},
      {"ToastViewManager",
        // Manager for creating and displaying toast modal on the client.
        TypeProps( Class:     "Shiny.Toast.ToastViewManager",
                   Interface: "Shiny.Core.Interfaces.IToastViewManager" )},
      {"BasicToastViewModel",
        // A generic view model for displaying a toast modal on the client.
        TypeProps( Class:     "Shiny.Toast.ViewModels.BasicToastViewModel",
                   Base:      "Shiny.Core.ViewModelBase",
                   Interface: "Shiny.IToastViewModel" )},
      //
      // PlayScene.dll
      //
      {"PlaySceneViewModel",
        // Global manager for active leagues, matches, or game sessions.
        TypeProps( Class:     "Shiny.Play.PlaySceneViewModel",
                   Base:      "Shiny.Core.SceneViewModel",
                   Interface: "Shiny.Core.Interfaces.IPlaySceneViewModel" )},
      //
      // WotC.MtGO.Client.Common.dll
      //
      {"ObjectProvider",
        // Global manager for all singleton objects registered with the client.
        TypeProps( Class:     "WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider" )},
      //
      // WotC.MtGO.Client.Model.Core.dll
      //
      {"Utility",
        // Provides filesystem paths to client data and assembly directories.
        TypeProps( Class:     "WotC.MtGO.Client.Model.Core.Utility" )},
      //
      // WotC.MtGO.Client.Model.Play.dll
      //
      {"Match",
        // Manager instance for an active match, used for all event types.
        TypeProps( Class:     "WotC.MtGO.Client.Model.Play.MatchEvent.Match",
                   Base:      "WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase" )},
    };

  /// <summary>
  /// Gets the MTGO assembly path for a given class for a given property type.
  /// </summary>
  public static string Get(string name, string key = "Class") =>
#pragma warning disable CS8603
    key switch
#pragma warning restore CS8603
    {
      "Class" =>
        Map[name].Item1,
      "Base" =>
        Map[name].Item2,
      "Interface" =>
        Map[name].Item3,
      _ =>
        throw new ArgumentException("Must specify a 'Class', 'Base', or 'Interface' key"),
    };
}
