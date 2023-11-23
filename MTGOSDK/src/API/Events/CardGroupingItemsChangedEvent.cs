/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;


namespace MTGOSDK.API;

/// <summary>
/// EventHandler wrapper types used by the API.
/// </summary>
/// <remarks>
/// This class contains wrapper types for events importable via
/// <br/>
/// <c>using static MTGOSDK.API.Events;</c>.
/// </remarks>
public sealed partial class Events
{
  //
  // EventHandler delegate types
  //

  /// <summary>
  /// Delegate type for subscribing to CardGrouping events changing items.
  /// </summary>
  public delegate void CardGroupingItemsChangedEventCallback(CardGroupingItemsChangedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered when a CardGrouping instance changes items.
  /// </summary>
  public class CardGroupingItemsChangedEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Collection.CardGroupingItemsChangedEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The unique identifier for this operation.
    /// </summary>
    public ulong? OperationId => @base.OperationId;

    /// <summary>
    /// The items added to the CardGrouping.
    /// </summary>
    public IEnumerable<CardQuantityPair> ItemsAdded =>
      Map<CardQuantityPair>(@base.ItemsAdded);

    /// <summary>
    /// The items removed from the CardGrouping.
    /// </summary>
    public IEnumerable<CardQuantityPair> ItemsRemoved =>
      Map<CardQuantityPair>(@base.ItemsRemoved);

    /// <summary>
    /// The items modified in the CardGrouping.
    /// </summary>
    public IEnumerable<CardQuantityPair> ItemsModified =>
      Map<CardQuantityPair>(@base.ItemsModified);
  }
}
