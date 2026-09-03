/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Collection;

/// <summary>
/// Controls how <see cref="CollectionManager.ImportDeck(Deck, DeckImportMode)"/>
/// handles an existing deck with the requested name.
/// </summary>
public enum DeckImportMode
{
  /// <summary>
  /// Always add a new deck, suffixing a conflicting name.
  /// </summary>
  Add = 0,

  /// <summary>
  /// Update a matching deck in place, or add a new deck when none exists.
  /// </summary>
  Replace = 1,
}
