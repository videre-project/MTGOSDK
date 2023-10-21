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

  /// <summary>
  /// The unique identifier for the SetType object.
  /// </summary>
  public string CardSetTypeCd => @base.CardSetTypeCd;

  /// <summary>
  /// The description of the SetType object (e.g. "Core Set", "Ancillary").
  /// </summary>
  public string Description => @base.Description;

  /// <summary>
  /// The enum flag value of the SetType object.
  /// </summary>
  public CardSetTypeEnum EnumValue => @base.EnumValue;

  //
  // CardSetType wrapper methods
  //

  /// <summary>
  /// Returns a CardSetType object from a given key.
  /// </summary>
  /// <param name="key">The CardSetTypeCd value of the SetType object.</param>
  /// <returns>A new SetType object.</returns>
  public static SetType GetFromKey(string key) =>
    new SetType(
      RemoteClient.InvokeMethod(new Proxy<CardSetType>(),
        methodName: "GetFromKey",
        genericTypes: null,
        args: key
      )
    );

  public override string ToString() => this.Description;

  public static implicit operator CardSetType(SetType setType) => setType.@base;
}
