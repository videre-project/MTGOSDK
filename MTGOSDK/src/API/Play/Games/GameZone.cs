/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;
using static MTGOSDK.API.Events;

/// <summary>
/// Represents a card zone in the game.
/// </summary>
[NonSerializable]
public sealed class GameZone(dynamic cardZone) : DLRWrapper<ICardZone>
{
  /// <summary>
  /// Stores an internal reference to the ICardZone object.
  /// </summary>
  internal override dynamic obj => Bind<ICardZone>(cardZone);

  private readonly dynamic m_cardZone = Retry(() => Unbind(cardZone).CardZone);

  //
  // ICardZone wrapper properties
  //

  /// <summary>
  /// The name of the zone.
  /// </summary>
  public string Name => m_cardZone.ToString();

  /// <summary>
  /// The number of cards in the zone.
  /// </summary>
  [NonSerializable]
  public int Count => @base.Count;

  /// <summary>
  /// The cards contained in the zone.
  /// </summary>
  [NonSerializable]
  public IEnumerable<GameCard> Cards => Map<GameCard>(@base);

  /// <summary>
  /// The enum value of the zone.
  /// </summary>
  [NonSerializable]
  public CardZone Zone => Cast<CardZone>(m_cardZone);

  /// <summary>
  /// The player this zone belongs to.
  /// </summary>
  public GamePlayer Player => new(@base.Player);

  //
  // ICardZone wrapper methods
  //

  public override string ToString() => this.Name;

  //
  // ICardZone wrapper events
  //

  /// <summary>
  /// Event triggered when a card is added, removed, or cleared from the zone.
  /// </summary>
  public EventProxy<GameZoneEventArgs> CollectionChanged =
    new(/* ICardZone */ cardZone, nameof(CollectionChanged));
}
