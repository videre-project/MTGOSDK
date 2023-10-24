/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Enums;


namespace MTGOSDK.API.Play.Events;

public sealed class Queue(dynamic queue) : Event<IQueue>
{
  /// <summary>
  /// Stores an internal reference to the IQueue object.
  /// </summary>
  internal override dynamic obj => Proxy<IQueue>.As(queue);

  //
  // IQueue wrapper properties
  //

  public QueueState CurrentState => @base.CurrentState;
}
