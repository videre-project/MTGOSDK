/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Play.Games;

public sealed class NamedValue<T>(dynamic namedValue)
    : DLRWrapper<INamedValue<T>>
{
  /// <summary>
  /// Stores an internal reference to the INamedValue&lt;T&gt; object.
  /// </summary>
  internal override dynamic obj => Bind<INamedValue<T>>(namedValue);

  //
  // INamedValue<T> wrapper properties
  //

  public string Name => @base.Name;

  public T Value => @base.Value;

  //
  // INamedValue<T> wrapper events
  //

  public EventProxy PropertyChanged =
    new(/* INamedValue<T> */ namedValue, nameof(PropertyChanged));
}
