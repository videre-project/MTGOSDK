/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using static MTGOSDK.API.Events;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class GameZone(dynamic cardZone) : DLRWrapper<ICardZone>
{
  /// <summary>
  /// Stores an internal reference to the ICardZone object.
  /// </summary>
  internal override dynamic obj => Bind<ICardZone>(cardZone);

  //
  // ICardZone wrapper properties
  //

  /// <summary>
  /// The name of the zone.
  /// </summary>
  public string Name => Unbind(@base).CardZone.ToString();

  /// <summary>
  /// The cards contained in the zone.
  /// </summary>
  public IEnumerable<GameCard> Cards => Map<GameCard>(@base);

  /// <summary>
  /// The enum value of the zone.
  /// </summary>
  /// <remarks>
  /// Requires the <c>WotC.MtGO.Client.Model.Play</c> reference assembly.
  /// </remarks>
  public CardZone Zone => Cast<CardZone>(Unbind(@base).CardZone);

  /// <summary>
  /// The player this zone belongs to.
  /// </summary>
  public GamePlayer Player => new(@base.Player);

  //
  // ICardZone wrapper events
  //

  /// <summary>
  /// Event triggered when a card is added, removed, or cleared from the zone.
  /// </summary>
  public EventProxy<GameZone, GameZoneEventArg> CollectionChanged =
    new(/* ICardZone */ cardZone, name: "CollectionChanged");
}
