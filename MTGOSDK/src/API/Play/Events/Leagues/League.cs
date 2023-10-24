/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Events.Leagues;

public sealed class League(dynamic league) : Event<ILeague>
{
  /// <summary>
  /// Stores an internal reference to the ILeague object.
  /// </summary>
  internal override dynamic obj => Proxy<ILeague>.As(league);

  //
  // ILeague wrapper properties
  //

  // TODO:
}
