/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;
using static MTGOSDK.API.Events;

public sealed class Queue(dynamic queue) : Event
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IQueue);

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
  public QueueState CurrentState =>
    Cast<QueueState>(Unbind(this).CurrentState);

  /// <summary>
  /// The event structure of the queue.
  /// </summary>
  public EventStructure EventStructure =>
    field ??= new(this, Unbind(this).TournamentStructure);

  //
  // IQueue wrapper events
  //

  public EventProxy<QueueStateEventArgs> QueueStateChanged =
    new(/* IQueue */ queue, nameof(QueueStateChanged));

  public EventProxy<QueueErrorEventArgs> QueueError =
    new(/* IQueue */ queue, nameof(QueueError));
}
