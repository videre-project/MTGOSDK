/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

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

  public string DeckRegionCd => @base.DeckRegionCd;

  public string Description => @base.Description;

  public DeckRegionEnum EnumValue => @base.EnumValue;

  //
  // DeckRegion wrapper methods
  //

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
