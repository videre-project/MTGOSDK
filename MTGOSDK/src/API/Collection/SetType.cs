/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using WotC.MTGO.Common;


namespace MTGOSDK.API.Collection;

public sealed class SetType(dynamic cardSetType) : DLRWrapper<CardSetType>
{
  /// <summary>
  /// Stores an internal reference to the CardSetType object.
  /// </summary>
  internal override dynamic obj => cardSetType;

  public SetType(string key) : this(cardSetType: GetFromKey(key)) { }

  //
  // CardSetType wrapper properties
  //

  public string CardSetTypeCd => @base.CardSetTypeCd;

  public string Description => @base.Description;

  public CardSetTypeEnum EnumValue => @base.EnumValue;

  //
  // CardSetType wrapper methods
  //

  public static DeckRegion GetFromKey(string key) =>
    new DeckRegion(
      RemoteClient.InvokeMethod(new Proxy<CardSetType>(),
        methodName: "GetFromKey",
        genericTypes: null,
        args: key
      )
    );

  public override string ToString() => this.Description;

  public static implicit operator CardSetType(SetType setType) => setType.@base;
}
