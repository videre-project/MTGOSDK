/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/


namespace MTGOInjector;

public static class MTGOTypes
{
  public static Tuple<string, string?, string?> TypeProps(
    string Class,
    string? Base=null,
    string? Interface=null)
  {
    return new (Class, Base, Interface);
  }

  public static Dictionary<string, Tuple<string, string?, string?>> Map =>
    new()
    {
      // Core
      {"DialogService",
        TypeProps( Class: "Shiny.Core.DialogManagement.DialogService",
                   Interface: "Shiny.Core.Interfaces.IDialogService" )},
      {"GenericDialogViewModel",
        TypeProps( Class: "Shiny.ViewModels.GenericDialogViewModel",
                   Base: "Shiny.ViewModels.BasicDialogViewModelBase" )},
      // FlsClient
      {"FlsClientSession",
        TypeProps( Class: "FlsClient.FlsClientSession",
                   Base: "FlsClient.ClientSessionBase",
                   Interface: "FlsClient.Interface.IFlsClientSession" )},
      // MTGO -> Shiny
      {"App",
        TypeProps( Class: "Shiny.App",
                   Base: "System.Windows.Application" )},
      // PlayScene -> Shiny.Play
      {"PlaySceneViewModel",
        TypeProps( Class: "Shiny.Play.PlaySceneViewModel",
                   Base: "Shiny.Core.SceneViewModel",
                   Interface: "Shiny.Core.Interfaces.IPlaySceneViewModel" )},
      // WotC.MtGO.Client.Common
      {"ObjectProvider",
        TypeProps( Class: "WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider" )},
      // WotC.MtGO.Client.Model.Core
      {"Utility",
        TypeProps( Class: "WotC.MtGO.Client.Model.Core.Utility" )},
      // WotC.MtGO.Client.Model.Play
      {"Match",
        TypeProps( Class: "WotC.MtGO.Client.Model.Play.MatchEvent.Match",
                   Base: "WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase" )},
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
