/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using MTGOSDK.API.Interface.Controls;
using MTGOSDK.API.Play.Games.Processors.Intermediate;
using System.Threading;

using MTGOSDK.API.Play.Games.Processors.Events;
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

  /// <summary>
  /// Signals prompt readiness after successful correlation or processor disposal.
  /// A failed capture or identity check resets it.
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

  public void Initialize(Game game)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    _game = game;
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
      LastPrompt = CaptureOptions(_game!, prompt).Prompt;
      PromptCorrelated.Set();
      context.Emit(new PromptChangedEventArgs(LastSnapshot, LastPrompt));
    }
    catch
    {
      PromptCorrelated.Reset();
      throw;
    }
  }

  private static GamePrompt MaterializePrompt(RemoteBatchValue value, GameContext context)
  {
    var captured = (RemoteBatchObject)RemoteBatchObject.FromValue(value)!;
    var options = new Dictionary<ActionType, IReadOnlyList<IPromptOption>>();
    foreach (var group in captured.GetDictionary<ActionType, object?[]>("Options"))
    {
      var actions = group.Value.Cast<RemoteBatchObject>()
        .Select(action =>
        {
          var resolved = (RemoteBatchObject)ResolveReferences(action, context)!;
          return GameAction.MaterializeCaptured(resolved);
        })
        .ToArray();
      if (actions.Any(action => action == null))
        throw new NotSupportedException("Captured prompt contains an unsupported action type.");
      options[group.Key] = Array.AsReadOnly(actions.Cast<IPromptOption>().ToArray());
    }
    return new GamePrompt(captured,
      new ReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>>(options));
  }

  private static object? ResolveReferences(object? value, GameContext context)
  {
    if (value is object?[] sequence)
      return sequence.Select(item => ResolveReferences(item, context)).ToArray();
    if (value is not RemoteBatchObject item)
      return value;
    if (item.NativeType?.FullName == "WotC.MtGO.Client.Model.Play.GameCard")
    {
      ulong address = item.LogicalAddress
        ?? throw new InvalidOperationException("Captured action card has no logical address.");
      var card = context.Current.Cards.Values.Concat(context.Current.HiddenCards.Values)
        .Concat(context.Current.RevealedCards.Values)
        .DistinctBy(card => ((GameCardPartial)Unbind(card)).LogicalAddress)
        .SingleOrDefault(card => ((object)Unbind(card)) is GameCardPartial partial
          && partial.LogicalAddress == address);
      // Auxiliary native cards have no server snapshot row. Keep their detached
      // identity and captured members rather than binding them to an unrelated ID.
      return card is null ? item : Unbind(card);
    }
    if (item.NativeType?.FullName == "WotC.MtGO.Client.Model.Play.GamePlayer")
    {
      int userId = item.Get<RemoteBatchObject>("User").Get<int>("Id");
      return Unbind(context.Current.Players.Values.Single(player => player.UserId == userId));
    }
    return new RemoteBatchObject(item.Members.ToDictionary(pair => pair.Key,
      pair => ResolveReferences(pair.Value, context)), item.NativeType, item.LogicalAddress);
  }

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
    batch.Output("prompt", batch.Read(instance, "Prompt"), prompt);

    // Capture resolved cards at the same native completion point as the prompt.
    // Reconstructing copies against the live game can mutate its zones.
    GameCardCapture.AppendGameplayCapture(batch, message, instance);
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

  public void Dispose()
  {
    _disposed = true;
    PromptCorrelated.Set();
  }

}
