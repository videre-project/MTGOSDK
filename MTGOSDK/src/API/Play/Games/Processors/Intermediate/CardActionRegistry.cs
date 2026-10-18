/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.Controls;
using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Batching;


namespace MTGOSDK.API.Play.Games.Processors.Intermediate;

/// <summary>
/// Exposes captured native actions and selection eligibility for one game.
/// </summary>
/// <remarks>
/// Actions and eligibility come from completed interaction captures paired with
/// their gameplay snapshots.
/// </remarks>
public sealed class CardActionRegistry : IDisposable
{
  private readonly object _stateLock = new();
  private ProcessorEvent<GameStateChangedEventArgs>? _stateChanged;
  private InteractionStateSnapshot _snapshot = InteractionStateSnapshot.Empty;
  private int _gameId;
  private long _version;
  private bool _disposed;

  //
  // Events and captured state
  //

  /// <summary>
  /// Raised when a card's captured native action list changes.
  /// </summary>
  /// <remarks>
  /// The card comes from the corresponding gameplay snapshot. Removed cards
  /// retain their last captured state and report an empty action list.
  /// </remarks>
  public event Action<(GameCard Card, IReadOnlyList<CardAction> Actions)>? Changed;

  /// <summary>
  /// Gets the game ID associated with this registry.
  /// </summary>
  public int GameId => _gameId;

  /// <summary>
  /// Gets one retained capture for reading actions and eligibility together.
  /// </summary>
  public InteractionStateSnapshot Snapshot
  {
    get
    {
      lock (_stateLock)
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _snapshot;
      }
    }
  }

  /// <summary>
  /// Gets a monotonically increasing registry version.
  /// </summary>
  public long Version => Interlocked.Read(ref _version);

  //
  // Initialization and captured reads
  //

  /// <summary>
  /// Attaches the registry to the game's correlated state publications.
  /// </summary>
  public void Initialize(Game game)
  {
    ArgumentNullException.ThrowIfNull(game);

    lock (_stateLock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      if (_stateChanged is not null)
        return;

      int gameId = game.Id;
      var processing = GameProcessor.ForGame(game);
      var stateChanged = game.OnStateChanged;
      stateChanged += OnStateChanged;
      _snapshot = processing.GetProcessor<PromptProcessor>()?.LastState?.Interaction
        ?? InteractionStateSnapshot.Empty;
      _gameId = gameId;
      _stateChanged = stateChanged;
    }
  }

  /// <summary>
  /// Gets a card's captured native actions. An absent card has no captured actions.
  /// </summary>
  /// <remarks>
  /// For a retained game update, use update.Interaction.Cards instead.
  /// These methods expose the registry's latest capture.
  /// </remarks>
  public IReadOnlyList<CardAction> GetActions(ulong logicalAddress)
  {
    lock (_stateLock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      return _snapshot.Cards.GetValueOrDefault(logicalAddress)?.Actions
        ?? Array.Empty<CardAction>();
    }
  }

  /// <summary>
  /// Gets the MTGO card IDs reported in the latest scene capture. IDs may repeat.
  /// </summary>
  public IReadOnlyList<int> GetRegisteredCardIds()
  {
    lock (_stateLock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      return _snapshot.Cards.Values.Select(card => card.CardId).ToArray();
    }
  }

  /// <summary>
  /// Gets a card's captured selection eligibility without IPC.
  /// </summary>
  public CardSelectable GetSelectable(ulong logicalAddress)
  {
    lock (_stateLock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      return _snapshot.Cards.GetValueOrDefault(logicalAddress)?.Selection
        ?? new(-1, false, false, false, false);
    }
  }

  /// <summary>
  /// Gets prompt buttons from the latest notification-driven UI capture.
  /// This read performs no IPC.
  /// </summary>
  public IReadOnlyList<PromptButton> GetPromptButtons()
  {
    lock (_stateLock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      return _snapshot.Buttons;
    }
  }

  /// <summary>
  /// Gets captured player selection flags, including each native server player ID.
  /// This read performs no IPC.
  /// </summary>
  public IReadOnlyList<PlayerSelectable> GetPlayerSelectable()
  {
    lock (_stateLock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      return _snapshot.Players;
    }
  }

  /// <summary>
  /// Gets actions for a live or captured card using its Diver logical address.
  /// </summary>
  public IReadOnlyList<CardAction> GetActions(GameCard card) =>
    GetActions(GetLogicalAddress(card) ?? 0);

  /// <summary>
  /// Gets selection flags for a live or captured card using its Diver logical address.
  /// </summary>
  public CardSelectable GetSelectable(GameCard card) =>
    GetSelectable(GetLogicalAddress(card) ?? 0);

  private static ulong? GetLogicalAddress(GameCard card)
  {
    ArgumentNullException.ThrowIfNull(card);
    object raw = DLRWrapper.Unbind(card);
    return raw switch
    {
      GameCardPartial partial => partial.LogicalAddress,
      RemoteBatchObject captured => captured.LogicalAddress,
      _ => card.@ro.RemoteToken
    };
  }

  //
  // Captured action-list notifications and lifetime cleanup
  //

  private void OnStateChanged(GameStateChangedEventArgs update)
  {
    var snapshot = update.Interaction;
    InteractionStateSnapshot previous;
    lock (_stateLock)
    {
      if (_disposed)
        return;
      previous = _snapshot;
      _snapshot = snapshot;
      Interlocked.Increment(ref _version);
    }

    // An empty action list is a real update, including cards removed from the scene.
    foreach (ulong address in previous.Cards.Keys.Union(snapshot.Cards.Keys))
    {
      previous.Cards.TryGetValue(address, out var oldCard);
      snapshot.Cards.TryGetValue(address, out var card);
      if (oldCard is not null && card is not null && oldCard.HasSameActions(card))
        continue;

      var actions = card?.Actions ?? Array.Empty<CardAction>();
      if (oldCard is null && actions.Count == 0)
        continue;
      if (card is null && oldCard!.Actions.Count == 0)
        continue;

      var owner = (card ?? oldCard!).Card;
      if (owner is null)
      {
        Log.Error("Correlated action card {0} has no gameplay snapshot in game {1}, nonce {2}.",
          address, _gameId, update.Nonce);
        continue;
      }

      var handlers = Changed;
      if (handlers is null)
        continue;
      foreach (Action<(GameCard Card, IReadOnlyList<CardAction> Actions)> handler in
        handlers.GetInvocationList())
      {
        try
        {
          handler((owner, actions));
        }
        catch (Exception error)
        {
          Log.Error(error, "Card action handler failed for logical address {0}.", address);
        }
      }
    }
  }

  public void Dispose()
  {
    ProcessorEvent<GameStateChangedEventArgs>? stateChanged;
    lock (_stateLock)
    {
      if (_disposed)
        return;

      _disposed = true;
      stateChanged = _stateChanged;
      _stateChanged = null;
      _gameId = 0;
      _snapshot = InteractionStateSnapshot.Empty;
    }

    if (stateChanged is not null)
      stateChanged -= OnStateChanged;

    Changed = null;
  }
}
