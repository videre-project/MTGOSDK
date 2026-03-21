/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class SelectFromListAction(dynamic selectFromListAction)
    : GameAction
{
  /// <summary>
  /// Stores an internal reference to the ISelectFromListAction object.
  /// </summary>
  internal override dynamic obj =>
    Bind<ISelectFromListAction>(selectFromListAction);

  public readonly struct NamedValue<T>
  {
    public string Name { get; init; }
    public T Value { get; init; }

    public NamedValue(dynamic namedValue)
    {
      var item = Bind<INamedValue<T>>(namedValue);
      Name = item.Name;
      Value = item.Value;
    }
  }

  //
  // ISelectFromListAction wrapper properties
  //

  public string ItemType => @base.ItemType;

  public IList<NamedValue<int>> AvailableItems =>
    Map<IList, NamedValue<int>>(@base.AvailableItems);

  public NamedValue<int> SelectedItem => new(@base.SelectedValue);
}
