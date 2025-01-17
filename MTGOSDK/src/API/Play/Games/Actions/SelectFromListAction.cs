/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

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

  //
  // ISelectFromListAction wrapper properties
  //

  public string ItemType => @base.ItemType;

  public IList<NamedValue<int>> AvailableItems =>
    Map<IList, NamedValue<int>>(@base.AvailableItems);

  public NamedValue<int> SelectedItem => new(@base.SelectedValue);
}
