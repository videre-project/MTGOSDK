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
        TypeProps( Class:    "Shiny.Core.RelayCommand",
                   Base:     "System.Windows.Input.ICommand" )},
      {"GameReplayService",
        // Global replay service for requesting and dispatching game replays.
        TypeProps( Class:     "Shiny.Play.GameReplayService",
                   Interface: "Shiny.Core.Interfaces.IGameReplayService" )},
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
      {"PlayerEventManager",
        // Global manager for all player events, including game joins and replays.
        TypeProps( Class:     "WotC.MtGO.Client.Model.Play.PlayerEventManager",
                   Interface: "WotC.MtGO.Client.Model.Play.IPlayerEventManager" )},
      {"PlayService",
        // Global manager for all active tournaments, leagues, or game sessions.
        TypeProps( Class: "WotC.MtGO.Client.Model.Play.PlayService",
                   Base: "WotC.MtGO.Client.Model.Core.ModelService",
                   Interface: "WotC.MtGO.Client.Model.Play.IPlay" )},
      //
      // WotC.MtGO.Client.Model.Settings.dll
      //
      {"SettingsService",
        // Global manager for all client settings, including user preferences.
        TypeProps( Class:     "WotC.MtGO.Client.Model.Settings.SettingsService",
                   Base:      "WotC.MtGO.Client.Model.Core.ModelService",
                   Interface: "WotC.MtGO.Client.Model.Settings.ISettings" )},
      {"GameHistoryManager",
        //
        TypeProps( Class:     "WotC.MtGO.Client.Model.Settings.GameHistoryManager",
                   Interface: "WotC.MtGO.Client.Model.Settings.IGameHistoryManager" )},
      //
      // SettingsScene.dll
      //
      {"GameHistoryViewModel",
        //
        TypeProps( Class:     "Shiny.Settings.ViewModels.GameHistoryViewModel",
                   Base:      "Shiny.Core.ViewModelBase",
                   Interface: "Shiny.Settings.Interfaces.IGameHistoryViewModel" )},
      {"GameHistoryDataViewModel",
        //
        TypeProps( Class:     "Shiny.Settings.ViewModels.GameHistoryDataViewModel",
                   Base:      "Shiny.Core.ViewModelBase",
                   Interface: "Shiny.Settings.Interfaces.IGameHistoryDataViewModel" )},
      {"HistoryGame",
        // Wrapper class for a single game's ID and position played in a match.
        TypeProps( Class:     "Shiny.Settings.ViewModels.HistoryGame",
                   Interface: "Shiny.Settings.Interfaces.IHistoryGame" )},
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
