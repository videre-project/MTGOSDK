/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Events;

public sealed class Match(dynamic match) : Event<IMatch>
{
  /// <summary>
  /// Stores an internal reference to the IMatch object.
  /// </summary>
  internal override dynamic obj => Proxy<IMatch>.As(match);

  //
  // IMatch wrapper properties
  //

  // TODO
}
