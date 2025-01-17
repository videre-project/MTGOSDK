/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class NumericAction(dynamic numericAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the INumericAction object.
  /// </summary>
  internal override dynamic obj => Bind<INumericAction>(numericAction);

  //
  // INumericAction wrapper properties
  //

  public int ChosenNumber => @base.ChosenNumber;

  public int Minimum => @base.Minimum;

  public int Maximum => @base.Maximum;

  public int Initial => @base.Initial;
}
