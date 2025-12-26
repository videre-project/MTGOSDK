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

  private Queue m_queue => field ??= new(Unbind(this));

  [RuntimeInternal]
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
  /// The name of the event or match.
  /// </summary>
  public string Name => @base.Description;

  /// <summary>
  /// A class describing the event format (e.g. Standard, Modern, Legacy, etc.).
  /// </summary>
  public PlayFormat Format => new(@base.PlayFormat);

  /// <summary>
  /// The event structure of the historical item.
  /// </summary>
  public EventStructure EventStructure =>
    field ??= new(m_queue, Unbind(this).TournamentStructure);

  /// <summary>
  /// The start time of the event or match.
  /// </summary>
  public DateTime StartTime => @base.StartTime;
}
