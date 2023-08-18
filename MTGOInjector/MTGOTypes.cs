/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/


namespace MTGOInjector;

public static class MTGOTypes
{
  public static Dictionary<string, Tuple<string, string?, string?>> Map =>
    new()
    {
      // Core
      {"DialogService",
        new ( "Shiny.Core.DialogManagement.DialogService",
              null,
              "Shiny.Core.Interfaces.IDialogService" )},
      {"GenericDialogViewModel",
        new ( "Shiny.ViewModels.GenericDialogViewModel",
              "Shiny.ViewModels.BasicDialogViewModelBase",
              null )},
      // FlsClient
      {"FlsClientSession",
        new ( "FlsClient.FlsClientSession",
              "FlsClient.ClientSessionBase",
              "FlsClient.Interface.IFlsClientSession" )},
      // MTGO -> Shiny
      {"App",
        new ( "Shiny.App",
              "System.Windows.Application",
              null )},
      // PlayScene -> Shiny.Play
      {"PlaySceneViewModel",
        new ( "Shiny.Play.PlaySceneViewModel",
              "Shiny.Core.SceneViewModel",
              "Shiny.Core.Interfaces.IPlaySceneViewModel" )},
      // WotC.MtGO.Client.Common
      {"ObjectProvider",
        new ( "WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider",
              null,
              null )},
      // WotC.MtGO.Client.Model.Core
      {"Utility",
        new ( "WotC.MtGO.Client.Model.Core.Utility",
              null,
              null )},
      // WotC.MtGO.Client.Model.Play
      {"Match",
        new ( "WotC.MtGO.Client.Model.Play.MatchEvent.Match",
              "WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase",
              null )},
    };

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
