/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.API.Play.Games.Processors.Events;

/// <summary>
/// Lazily registers a state processor on the game's processing scope when
/// the first handler is subscribed. Supports the usual += and -= syntax.
/// </summary>
public sealed class ProcessorEvent<T> where T : GameEventArgs
{
  private readonly Game _game;
  private readonly Type _processorType;
  private readonly Func<IGameStateProcessor> _factory;
  private readonly object _lock = new();
  private GameProcessor? _processor;
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
    lock (e._lock)
    {
      var processor = GameProcessor.ForGame(e._game);
      if (!ReferenceEquals(e._processor, processor))
      {
        processor.EnsureRegistered(e._processorType, e._factory);
        processor.On<T>(args => e.Dispatch(processor, args));
        processor.RegisterEventCleanup(() => e.ClearFor(processor));
        e._processor = processor;
      }
      e._handlers += handler;
    }
    return e;
  }

  public static ProcessorEvent<T> operator -(
    ProcessorEvent<T> e,
    Action<T> handler)
  {
    lock (e._lock)
    {
      e._handlers -= handler;
    }
    return e;
  }

  private void Dispatch(GameProcessor processor, T args)
  {
    Action<T>? handlers;
    lock (_lock)
      handlers = ReferenceEquals(_processor, processor) ? _handlers : null;
    if (handlers is null)
      return;
    foreach (Action<T> handler in handlers.GetInvocationList())
    {
      try
      {
        handler(args);
      }
      catch (Exception error)
      {
        Log.Error(error, "Event handler failed for {0}.", typeof(T).Name);
      }
    }
  }

  private void ClearFor(GameProcessor processor)
  {
    lock (_lock)
    {
      // Cleanup of an old scope must not clear a newly subscribed scope.
      if (!ReferenceEquals(_processor, processor))
        return;
      _handlers = null;
      _processor = null;
    }
  }

  /// <summary>
  /// Removes handlers while retaining registration with the current scope.
  /// A later subscription does not create a second dispatch registration.
  /// </summary>
  public void Clear()
  {
    lock (_lock)
    {
      _handlers = null;
    }
  }
}
