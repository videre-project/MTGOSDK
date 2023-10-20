/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;

public sealed class Binder(dynamic binder) : CardGrouping<IBinder>
{
  /// <summary>
  /// Stores an internal reference to the IBinder object.
  /// </summary>
  internal override dynamic obj => binder;

  //
  // IBinder wrapper properties
  //

  public bool IsLastUsedBinder => @base.IsLastUsedBinder;

  public bool IsWishList => @base.IsWishList;

  public bool IsMegaBinder => @base.IsMegaBinder;
}
