/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.Games.Processors.EventArgs;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Event wrapper that lazily registers an <see cref="IGameStateProcessor"/>
/// on the parent game's <see cref="GameProcessor"/> when the first handler
/// is subscribed. Follows the same <c>+=</c> / <c>-=</c> operator pattern
/// as <see cref="MTGOSDK.Core.Reflection.EventHookWrapper{I}"/>.
/// </summary>
/// <typeparam name="T">The event args type emitted by the processor.</typeparam>
public sealed class ProcessorEvent<T> where T : GameEventArgs
{
  private readonly Game _game;
  private readonly Type _processorType;
  private readonly Func<IGameStateProcessor> _factory;
  private Action<T>? _handlers;

  internal ProcessorEvent(
    Game game,
    Type processorType,
    Func<IGameStateProcessor> factory)
  {
    _game = game;
    _processorType = processorType;
    _factory = factory;
  }

  public static ProcessorEvent<T> operator +(
    ProcessorEvent<T> e,
    Action<T> handler)
  {
    if (e._handlers == null)
    {
      // First subscriber: activate the processor pipeline.
      var gp = e._game.EnsureProcessor();
      gp.EnsureRegistered(e._processorType, e._factory);
      gp.On<T>(args => e._handlers?.Invoke(args));
    }

    e._handlers += handler;
    return e;
  }

  public static ProcessorEvent<T> operator -(
    ProcessorEvent<T> e,
    Action<T> handler)
  {
    e._handlers -= handler;
    return e;
  }

  /// <summary>
  /// Removes all handlers.
  /// </summary>
  public void Clear() => _handlers = null;
}
