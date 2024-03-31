/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MTGO.Common;


namespace MTGOSDK.API.Collection;

public sealed class DeckRegion(dynamic deckregion)
    : DLRWrapper<WotC.MTGO.Common.DeckRegion>
{
  /// <summary>
  /// Stores an internal reference to the DeckRegion object.
  /// </summary>
  internal override dynamic obj => deckregion;

  public DeckRegion(string key) : this(deckregion: GetFromKey(key)) { }

  //
  // DeckRegion wrapper properties
  //

  /// <summary>
  /// The unique identifier for the DeckRegion object.
  /// </summary>
  public string Key => @base.DeckRegionCd;

  /// <summary>
  /// The description of the DeckRegion object (e.g. "MainDeck", "Sideboard").
  /// </summary>
  public string Description => @base.Description;

  /// <summary>
  /// The enum flag value of the DeckRegion object.
  /// </summary>
  /// <remarks>
  /// Requires the <c>WotC.MTGO.Common</c> reference assembly.
  /// </remarks>
  public DeckRegionEnum EnumValue =>
    Cast<DeckRegionEnum>(Unbind(@base).EnumValue);

  //
  // DeckRegion wrapper methods
  //

  /// <summary>
  /// Returns a DeckRegion object from a given key.
  /// </summary>
  /// <param name="key">The DeckRegionCd value of the DeckRegion object.</param>
  /// <returns>A new DeckRegion object.</returns>
  public static DeckRegion GetFromKey(string key) =>
    new DeckRegion(
      RemoteClient.InvokeMethod(new Proxy<WotC.MTGO.Common.DeckRegion>(),
        methodName: "GetFromKey",
        genericTypes: null,
        args: key
      )
    );

  public override string ToString() => this.Description;

  public static implicit operator WotC.MTGO.Common.DeckRegion(DeckRegion region) =>
    region.@base;
}
