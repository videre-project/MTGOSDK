/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Represents a historical game, match, or tournament.
/// </summary>
public abstract class HistoricalItem<I, T> : DLRWrapper<IHistoricalItem>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  internal override Type type => typeof(I);

  public sealed class Default(dynamic historicalItem)
      : HistoricalItem<IHistoricalItem, dynamic>
  {
    /// <summary>
    /// Stores an internal reference to the IHistoricalItem object.
    /// </summary>
    internal override dynamic obj => Bind<IHistoricalItem>(historicalItem);
  }

  //
  // IHistoricalItem wrapper properties
  //

  /// <summary>
  /// The event's tournament ID (if applicable, otherwise the match ID).
  /// </summary>
  public int Id => @base.EventId;

  /// <summary>
  /// The session token for the event or match.
  /// </summary>
  public Guid Token => Cast<Guid>(Unbind(@base).EventToken);

  /// <summary>
  /// The event object (e.g. League, Tournament, Match, etc.).
  /// </summary>
  public T Event => Event<IPlayerEvent>.FromPlayerEvent(@base.PlayerEvent);

  /// <summary>
  /// The start time of the event or match.
  /// </summary>
  public DateTime StartTime => @base.StartTime;
}
