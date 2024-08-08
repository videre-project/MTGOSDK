/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics.CodeAnalysis;

using MTGOSDK.Core.Reflection;

// using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Represents a historical game, match, or tournament.
/// </summary>
public abstract class HistoricalItem<T> : DLRWrapper<IHistoricalItem>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(T);

  [ExcludeFromCodeCoverage]
  public sealed class Default(dynamic historicalItem) : HistoricalItem<dynamic>
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

  // FIXME: Historical items do not have an actual PlayerEvent
  // /// <summary>
  // /// The event object (e.g. League, Tournament, Match, etc.).
  // /// </summary>
  // public T Event => EventManager.PlayerEventFactory(@base.PlayerEvent);

  /// <summary>
  /// The start time of the event or match.
  /// </summary>
  public DateTime StartTime => @base.StartTime;
}
