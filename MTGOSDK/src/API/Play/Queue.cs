/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Enums;


namespace MTGOSDK.API.Play;
using static MTGOSDK.API.Events;

public sealed class Queue(dynamic queue) : Event<IQueue>
{
  /// <summary>
  /// Stores an internal reference to the IQueue object.
  /// </summary>
  internal override dynamic obj => Bind<IQueue>(queue);

  //
  // IQueue wrapper properties
  //

  /// <summary>
  /// The current state of the queue (e.g. JoinRequested, Joined, Closed, etc.).
  /// </summary>
  /// <remarks>
  /// Requires the <c>MTGOSDK.Ref.dll</c> reference assembly.
  /// </remarks>
  [Default(QueueState.NotSet)]
  public QueueState CurrentState =>
    Cast<QueueState>(Unbind(@base).CurrentState);

  //
  // IQueue wrapper events
  //

  public EventProxy<QueueStateEventArgs> QueueStateChanged =
    new(/* IQueue */ queue, nameof(QueueStateChanged));

  public EventProxy<QueueErrorEventArgs> QueueError =
    new(/* IQueue */ queue, nameof(QueueError));
}
