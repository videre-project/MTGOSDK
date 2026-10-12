/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using MTGOSDK.API.Interface.Controls;
using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Intermediate;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Types;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Validates and publishes prompts captured with their gameplay message.
/// </summary>
public sealed class PromptProcessor : IGameStateProcessor, IDisposable
{
  private Game? _game;
  private volatile bool _disposed;
  private int _gameId;
  private readonly object _stateLock = new();
  private readonly Dictionary<int, InteractionStateSnapshot> _interactions = new();
  private readonly Dictionary<(Guid Stream, long Revision), List<InteractionStateSnapshot>> _pendingInteractions = new();
  private InteractionModeTracker? _interactionTracker;
  private GameContext? _completedContext;
  private GamePrompt? _completedPrompt;
  private GameStateChangedEventArgs? _lastState;
  private long _stateSequence;

  //
  // Captured state
  //

  /// <summary>
  /// The most recent combined game and interaction update.
  /// </summary>
  public GameStateChangedEventArgs? LastState => Volatile.Read(ref _lastState);

  /// <summary>
  /// Signals prompt readiness. A failed capture or identity check resets it.
  /// Successful correlation or processor disposal releases waiting callers.
  /// </summary>
  internal ManualResetEventSlim PromptCorrelated { get; } = new(true);

  /// <summary>
  /// The most recent snapshot received from the processor pipeline.
  /// </summary>
  public GameStateSnapshot? LastSnapshot { get; private set; }

  /// <summary>
  /// The most recent prompt materialized from a gameplay-hook capture.
  /// </summary>
  public GamePrompt? LastPrompt { get; private set; }

  //
  // Initialization and prompt options
  //

  public void Initialize(Game game)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    _game = game;
    _gameId = game.Id;
    _interactionTracker = GameProcessor.ForGame(game).InteractionState;
    _interactionTracker.StateChanged += OnInteractionChanged;
    OnInteractionChanged(_interactionTracker.Snapshot);
  }

  /// <summary>
  /// Captures a prompt's options and client UI revision for this processor's game.
  /// </summary>
  public PromptOptionsSnapshot CaptureOptions(GamePrompt prompt) =>
    CaptureOptions(
      _game ?? throw new InvalidOperationException("The prompt processor is not initialized."),
      prompt);

  /// <summary>
  /// Combines the supplied prompt actions with captured client-local controls
  /// without registering a snapshot processor. The retained options may reference
  /// SDK card and player objects.
  /// </summary>
  /// <remarks>
  /// Only a matching captured native prompt receives client controls. Historical
  /// prompts retain their native actions and are never paired with newer UI buttons.
  /// Reads the notification-driven interaction capture without refreshing live UI state.
  /// </remarks>
  public static PromptOptionsSnapshot CaptureOptions(Game game, GamePrompt prompt)
  {
    ArgumentNullException.ThrowIfNull(game);
    ArgumentNullException.ThrowIfNull(prompt);

    return CaptureOptions(prompt, GameProcessor.ForGame(game).InteractionState.Snapshot);
  }

  internal static PromptOptionsSnapshot CaptureOptions(
    GamePrompt prompt, InteractionStateSnapshot interaction)
  {
    int nonce = prompt.Nonce;
    var options = prompt.Options
      .Where(group => group.Key != ActionType.PromptControl)
      .ToDictionary(group => group.Key, group => group.Value);

    IReadOnlyList<PromptButton> buttons = interaction.PromptNonce == nonce
      ? interaction.Buttons : Array.Empty<PromptButton>();
    var controls = buttons
      .Where(button => !button.HasGameAction
        && !string.IsNullOrWhiteSpace(button.ButtonType)
        && !string.IsNullOrWhiteSpace(button.Name ?? button.Color))
      .Select(button => (IPromptOption)new PromptControl(
        button.Id,
        button.Name ?? button.Color!,
        button.Enabled,
        button.ButtonType,
        button.Color))
      .ToArray();
    if (controls.Length > 0)
      options[ActionType.PromptControl] = Array.AsReadOnly(controls);

    var signature = new StringBuilder();
    signature.Append(nonce);
    signature.Append(':').Append(interaction.Mode);
    foreach (var button in buttons)
      signature.Append('|').Append(button.Id.Group).Append(':')
        .Append(button.Id.Index).Append(':').Append(button.ButtonType)
        .Append(':').Append(button.Name).Append(':').Append(button.Color)
        .Append(':').Append(button.Enabled).Append(':')
        .Append(button.HasGameAction);
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString()));

    var capturedPrompt = new GamePrompt((object)Unbind(prompt),
      new ReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>>(options));

    return new PromptOptionsSnapshot(nonce, BitConverter.ToInt64(hash), capturedPrompt);
  }

  //
  // Gameplay and interaction updates
  //

  public void Process(GameContext context)
  {
    if (_disposed)
      return;
    LastSnapshot = context.Current;
    try
    {
      var captured = context.CapturedPrompt
        ?? throw new InvalidOperationException("Gameplay callback omitted its captured prompt.");
      if (captured.Kind == RemoteBatchValueKind.Null)
      {
        if (LastSnapshot.InteractionTimestamp != 0 || LastSnapshot.PromptedPlayer != byte.MaxValue ||
            !string.IsNullOrEmpty(LastSnapshot.PromptText))
          throw new InvalidOperationException("Gameplay message has a prompt identity but native capture has no prompt.");
        LastPrompt = null;
        PromptCorrelated.Set();
        return;
      }
      var prompt = MaterializePrompt(captured, context);
      if (prompt.Timestamp != LastSnapshot.InteractionTimestamp ||
          prompt.PromptedPlayer != LastSnapshot.PromptedPlayer || prompt.Text != LastSnapshot.PromptText)
        throw new InvalidOperationException("Captured prompt identity differs from the gameplay snapshot.");
      var interaction = GetInteraction(context);
      LastPrompt = CaptureOptions(prompt, interaction).Prompt;
      PromptCorrelated.Set();
      context.Emit(new PromptChangedEventArgs(LastSnapshot, LastPrompt));
    }
    catch
    {
      PromptCorrelated.Reset();
      throw;
    }
  }

  /// <summary>
  /// Publishes after the current gameplay message's processors have finished.
  /// </summary>
  internal void Complete(GameContext context)
  {
    lock (_stateLock)
    {
      if (_disposed)
        return;
      if (LastPrompt is null)
      {
        _completedContext = null;
        _completedPrompt = null;
        return;
      }
      if (LastPrompt.Nonce != context.Current.Nonce)
        return;

      // Validate the UI capture paired with this native gameplay completion.
      var boundary = context.CaptureSequence;
      var interaction = GetInteraction(context);
      bool requiresCapture = boundary is { NotificationSequence: > 0 }
        && (_interactionTracker is null || _interactionTracker.RequiresCapture(boundary));
      if (requiresCapture && interaction.CaptureSequence != boundary!.NotificationSequence)
      {
        PromptCorrelated.Reset();
        throw new InvalidOperationException(
          $"Gameplay revision {boundary.StateRevision} has no matching interaction capture {boundary.NotificationSequence}.");
      }

      // Publish the completed server snapshot before its later local transitions.
      _completedContext = context;
      _completedPrompt = LastPrompt;
      _interactions[context.Current.Nonce] = interaction;
      PublishState(context, LastPrompt, interaction);

      // Completed local transitions can arrive while the server pipeline is still processing.
      // Publish each against its owning message after the boundary capture, in native order.
      if (boundary is not null)
      {
        if (_pendingInteractions.TryGetValue((boundary.StreamId, boundary.StateRevision), out var local))
        {
          foreach (var capture in local.Where(capture => capture.CaptureSequence > boundary.NotificationSequence))
          {
            _interactions[context.Current.Nonce] = capture;
            PublishState(context, LastPrompt, capture);
          }
        }

        // Retire consumed revisions while retaining captures for later messages.
        foreach (var key in _pendingInteractions.Keys.Where(key => key.Stream == boundary.StreamId
          && key.Revision <= boundary.StateRevision).ToArray())
          _pendingInteractions.Remove(key);
      }

      // Retain captures for newer prompts which arrived ahead of their gameplay callback.
      foreach (int nonce in _interactions.Where(entry => entry.Key != context.Current.Nonce
        && entry.Value.Version < interaction.Version).Select(entry => entry.Key).ToArray())
        _interactions.Remove(nonce);
    }
  }

  private InteractionStateSnapshot GetInteraction(GameContext context)
  {
    lock (_stateLock)
    {
      if (context.CaptureSequence is { } sequence
        && _pendingInteractions.TryGetValue((sequence.StreamId, sequence.StateRevision), out var pending))
      {
        var paired = pending.FirstOrDefault(capture => capture.PromptNonce == context.Current.Nonce
          && capture.CaptureSequence == sequence.NotificationSequence);
        if (paired is not null)
          return paired;
      }

      var interaction = _interactions.GetValueOrDefault(context.Current.Nonce);
      if (interaction is null)
        return InteractionStateSnapshot.Empty;

      // No relevant native notification means the previous captured UI values remain current.
      return context.CaptureSequence is null or { NotificationSequence: 0 }
        || MatchesRevision(interaction, context) ? interaction : InteractionStateSnapshot.Empty;
    }
  }

  private static bool MatchesRevision(InteractionStateSnapshot interaction, GameContext context) =>
    context.CaptureSequence is { } sequence && interaction.CaptureStream == sequence.StreamId
      && interaction.StateRevision == sequence.StateRevision;

  private void OnInteractionChanged(InteractionStateSnapshot interaction)
  {
    lock (_stateLock)
    {
      if (_disposed || interaction.PromptNonce is not int nonce)
        return;

      // A newer native revision may arrive before its gameplay callback is processed.
      // Retain it by invocation identity even when the prompt nonce is unchanged.
      if (interaction.StateRevision != 0 && (_completedContext is null
        || !MatchesRevision(interaction, _completedContext)))
      {
        var key = (interaction.CaptureStream, interaction.StateRevision);
        if (!_pendingInteractions.TryGetValue(key, out var pending))
        {
          pending = new();
          _pendingInteractions.Add(key, pending);
        }
        pending.Add(interaction);
        return;
      }

      _interactions[nonce] = interaction;
      if (_completedContext?.Current.Nonce == nonce && _completedPrompt is not null)
        PublishState(_completedContext, _completedPrompt, interaction);
    }
  }

  private void PublishState(GameContext context, GamePrompt prompt,
    InteractionStateSnapshot interaction)
  {
    var previous = _lastState;
    if (previous is not null && ReferenceEquals(previous.Context, context)
      && previous.Interaction.Mode == interaction.Mode
      && previous.Interaction.PromptBoxText == interaction.PromptBoxText
      && previous.Interaction.Buttons.SequenceEqual(interaction.Buttons)
      && previous.Interaction.Players.SequenceEqual(interaction.Players)
      && previous.Interaction.Cards.SequenceEqual(interaction.Cards))
      return;

    // Server timestamps can repeat across messages. Include the emitted revision.
    var hash = new HashCode();
    hash.Add(_gameId);
    hash.Add(++_stateSequence);
    hash.Add(context.Current.Nonce);
    hash.Add(context.Current.Timestamp);
    hash.Add(interaction.Mode);
    hash.Add(interaction.PromptBoxText, StringComparer.Ordinal);
    foreach (var button in interaction.Buttons)
      hash.Add(button);
    foreach (var player in interaction.Players)
      hash.Add(player);
    foreach (var card in interaction.Cards)
      hash.Add(card);

    var options = CaptureOptions(prompt, interaction);

    // Resolve action card/player identities against this event's server snapshot.
    // The native action data and eligibility stay unchanged by this local binding.
    var cardLookup = CreateCardLookup(context);
    var cards = interaction.Cards.ToDictionary(pair => pair.Key,
      pair => pair.Value.Resolve(value => ResolveReferences(value, context, cardLookup)));
    interaction = interaction with
    {
      Cards = new ReadOnlyDictionary<ulong, CardInteractionSnapshot>(cards)
    };
    var update = new GameStateChangedEventArgs(options.Prompt, interaction, options)
    {
      Context = context,
      Nonce = hash.ToHashCode()
    };
    Volatile.Write(ref _lastState, update);
    context.Processor?.Dispatch(update, context, update.Nonce);
  }

  //
  // Captured prompt materialization
  //

  private static GamePrompt MaterializePrompt(RemoteBatchValue value, GameContext context)
  {
    var captured = (RemoteBatchObject)RemoteBatchObject.FromValue(value)!;
    var cards = CreateCardLookup(context);
    var options = new Dictionary<ActionType, IReadOnlyList<IPromptOption>>();
    foreach (var group in captured.GetDictionary<ActionType, object?[]>("Options"))
    {
      var actions = group.Value.Cast<RemoteBatchObject>()
        .Select(action =>
        {
          var resolved = (RemoteBatchObject)ResolveReferences(action, context, cards)!;
          return (IPromptOption)GameAction.MaterializeCaptured(resolved);
        })
        .ToArray();
      options[group.Key] = Array.AsReadOnly(actions);
    }
    return new GamePrompt(captured,
      new ReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>>(options));
  }

  private static Dictionary<ulong, GameCardPartial> CreateCardLookup(GameContext context)
  {
    var cards = new Dictionary<ulong, GameCardPartial>();
    foreach (var card in context.Current.Cards.Values
      .Concat(context.Current.HiddenCards.Values)
      .Concat(context.Current.RevealedCards.Values))
    {
      var partial = (GameCardPartial)Unbind(card);
      if (partial.LogicalAddress is ulong address)
        cards.TryAdd(address, partial);
    }
    return cards;
  }

  private static object? ResolveReferences(object? value, GameContext context,
    IReadOnlyDictionary<ulong, GameCardPartial> cards)
  {
    if (value is object?[] sequence)
      return sequence.Select(item => ResolveReferences(item, context, cards)).ToArray();
    if (value is not RemoteBatchObject item)
      return value;
    if (item.NativeType?.FullName == "WotC.MtGO.Client.Model.Play.GameCard")
    {
      ulong address = item.LogicalAddress
        ?? throw new InvalidOperationException("Captured action card has no logical address.");
      // Auxiliary native cards have no server snapshot row. Keep their detached
      // identity and captured members rather than binding them to an unrelated ID.
      return cards.TryGetValue(address, out var card) ? card : item;
    }
    if (item.NativeType?.FullName == "WotC.MtGO.Client.Model.Play.GamePlayer")
    {
      int userId = item.Get<RemoteBatchObject>("User").Get<int>("Id");
      return Unbind(context.Current.Players.Values.Single(player => player.UserId == userId));
    }
    return new RemoteBatchObject(item.Members.ToDictionary(pair => pair.Key,
      pair => ResolveReferences(pair.Value, context, cards)), item.NativeType, item.LogicalAddress);
  }

  //
  // Native gameplay capture
  //

  /// <summary>
  /// Builds the projection captured when native gameplay processing completes.
  /// The hook executes it in the client before delivering the callback.
  /// </summary>
  internal static RemoteHookCapture CreateGameplayCapture()
  {
    var scalar = RemoteBatchProjection.Value();
    var action = GameAction.CreateCaptureProjection();

    // Preserve the prompt's native action groups in the detached value.
    var prompt = RemoteBatchProjection.ObjectScalars();
    prompt.Members["Timestamp"] = new() { Path = "Timestamp", Projection = scalar };
    prompt.Members["Options"] = new()
    {
      Path = "Options",
      Projection = RemoteBatchProjection.Dictionary(
        scalar, RemoteBatchProjection.Sequence(action))
    };

    // The hook supplies its game instance (-1) and gameplay message (argument 0).
    var batch = new RemoteBatch();
    var instance = batch.Input(ObjectOrRemoteAddress.Null);
    var message = batch.Input(ObjectOrRemoteAddress.Null);

    batch.Output("gameId", batch.Read(message, "GameID"));
    batch.Output("timestamp", batch.Read(message, "GameStateTimestamp"));
    batch.Output("turnNumber", batch.Read(instance, "CurrentTurn"));
    batch.Output("phase", batch.Read(instance, "CurrentPhase"));
    batch.Output("priorityPlayer", batch.Read(message, "PriorityPlayer"));
    batch.Output("stateElements", batch.Read(message, "OtherStateElements"),
      RemoteBatchProjection.Sequence(CreateStateElementProjection()));
    batch.Output("players", batch.Read(instance, "PlayersInServerOrder"),
      RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
        ("Name", "User.Name", scalar))));
    batch.Output("prompt", batch.Read(instance, "Prompt"), prompt);

    // Capture resolved cards at the same native completion point as the prompt.
    // Reconstructing copies against the live game can mutate its zones.
    GameCardCapture.AppendGameplayCapture(batch, message, instance,
      GameProcessor.HashExcludedPropertyNames);
    return new()
    {
      Batch = batch.ToRequest(),
      Inputs = [-1, 0],
      CaptureOnExit = true,
      Scope = new()
      {
        Group = InteractionModeTracker.CaptureGroup,
        OwnerInput = 0,
        IsStateBoundary = true
      }
    };
  }

  private static RemoteBatchProjection CreateStateElementProjection()
  {
    var scalar = RemoteBatchProjection.Value();
    var sequence = RemoteBatchProjection.Sequence();
    var type = (Name: "Type", Path: "Type", Projection: scalar);
    var projection = RemoteBatchProjection.Object(type);

    // Unhandled elements still retain their type and position in the message.
    // Each recognized native type adds only the fields its processor consumes.
    const string prefix = "WotC.MtGO.Client.Model.Play.";
    projection.RuntimeTypes[prefix + "PlayerStatusElement"] = RemoteBatchProjection.Object(type,
      ("ActivePlayer",     "ActivePlayer",     scalar),
      ("Lifes",            "Lifes",            sequence),
      ("HandCounts",       "HandCounts",       sequence),
      ("LibraryCounts",    "LibraryCounts",    sequence),
      ("GraveyardCounts",  "GraveyardCounts",  sequence),
      ("TimeLeft",         "TimeLeft",         sequence));

    var prompt = RemoteBatchProjection.Object(type,
      ("TurnNumber",       "TurnNumber",       scalar),
      ("CurrentPhase",     "CurrentPhase",     scalar),
      ("PromptedPlayer",   "PromptedPlayer",   scalar),
      ("PromptText",       "PromptText",       scalar),
      ("TimeStamp",        "TimeStamp",        scalar));
    projection.RuntimeTypes[prefix + "TurnStepElement"] = prompt;
    projection.RuntimeTypes[prefix + "InteractStateElement"] = prompt;

    projection.RuntimeTypes[prefix + "ManaPoolElement"] = RemoteBatchProjection.Object(type,
      ("PlayerNumber", "PlayerNumber", scalar),
      ("Mana", "Mana", RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
        ("Color",  "Color",  scalar),
        ("Amount", "Amount", scalar)))));

    projection.RuntimeTypes[prefix + "DamageAssignmentElement"] = RemoteBatchProjection.Object(type,
      ("DamagingThingId", "DamagingThingId", scalar),
      ("DamageToDeal",    "DamageToDeal",    scalar),
      ("ThingsToDamage", "ThingsToDamage", RemoteBatchProjection.Sequence(
        RemoteBatchProjection.Object(
          ("ThingId", "ThingId", scalar),
          ("Amount",  "Amount",  scalar),
          ("Minimum", "Minimum", scalar),
          ("Maximum", "Maximum", scalar)))));
    return projection;
  }

  //
  // Lifetime cleanup
  //

  public void Dispose()
  {
    lock (_stateLock)
    {
      _disposed = true;
      if (_interactionTracker is not null)
        _interactionTracker.StateChanged -= OnInteractionChanged;
      _interactions.Clear();
      _pendingInteractions.Clear();
      _completedContext = null;
      _completedPrompt = null;
    }
    PromptCorrelated.Set();
  }

  public static void EnsureHookInitialized() => GameProcessor.EnsureHookInitialized();
}
