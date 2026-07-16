/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.API.Collection;

/// <summary>
/// The values returned by the bulk card-grouping item projection.
/// </summary>
public interface ICardGroupingItemSnapshot
{
  int CatalogId { get; }

  uint Annotation { get; }

  int Quantity { get; }
}

/// <summary>
/// An immutable, catalog-only view of an item in a card grouping.
/// </summary>
public readonly record struct CardGroupingItemSnapshot(
  int CatalogId,
  DeckRegion Region,
  uint Annotation,
  int Quantity) : ICardGroupingItemSnapshot;
