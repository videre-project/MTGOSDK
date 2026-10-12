/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.ObjectModel;

using MTGOSDK.API.Interface.Controls;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Types;
using UIDispatcher = MTGOSDK.API.Interface.Dispatcher;


namespace MTGOSDK.API.Play.Games.Processors.Intermediate;

/// <summary>
/// Tracks captured interaction modes, prompt controls, and selection eligibility.
/// </summary>
/// <remarks>
/// <para>
/// MTGO updates the mode, displayed prompt text, controls, and card actions through
/// separate notifications. Hooks around scene and prompt-box operations group those
/// notifications. The Diver copies their resulting values when the outermost hooked
/// call returns, before delivering the capture to the SDK. A notification outside
/// those calls is captured when it occurs.
/// </para>
/// <para>
/// The tracker applies captures in their native stream order, even if callbacks
/// arrive out of order. Each snapshot retains its prompt identity and state revision
/// so that PromptProcessor can correlate it with the server state.
/// </para>
/// <para>
/// Shared method hooks observe card, player, and button property changes without
/// subscribing to each view model. Per-scene subscriptions observe prompt text and
/// collection changes. Initial capture waits for the duel view to load and its
/// dispatcher to reach idle, including when attachment follows the load event.
/// </para>
/// </remarks>
public sealed class InteractionModeTracker : IDisposable
{

  //
  // Native hooks shared by the per-game trackers.
  //

  internal const string CaptureGroup = "MTGOSDK.GameInteractions";
  private const string SceneType = "Shiny.Play.Duel.ViewModel.DuelSceneViewModel";
  private const string PromptBoxType = "Shiny.Play.Duel.ViewModel.PromptBoxViewModel";

  // The shared notification method observes new view models as soon as MTGO uses
  // them. Native filtering resolves their scene before enrolling the capture.
  private static EventHookProxy<string, HookContext> s_propertiesChanged = CreatePropertyHook();

  private static EventHookProxy<string, HookContext> CreatePropertyHook()
  {
    var batch = CreateCaptureBatch(ObjectOrRemoteAddress.Null);
    return new(
      "WotC.MtGO.Client.Common.ChangeNotification.NotifyPropertyChangedImplementation",
      "OnPropertyChanged",
      new CapturedEventHook((context, _, _) => ("PropertyChanged", context)),
      new RemoteHookCapture
      {
        Batch = batch.ToRequest(),
        Inputs = [-1],
        IncludeParameters = false,
        Scope = CaptureScope(),
        CaptureOnExit = true,
        Filters =
        [
          new()
          {
            TypeFullName = "Shiny.Play.Duel.ViewModel.DuelSceneCardViewModel",
            InstancePath = "MainViewModel",
            ArgumentValues = ["IsSelectable", "IsTargetable", "IsTargeted", "HasActions", "", null]
          },
          new()
          {
            TypeFullName = "Shiny.Play.Duel.ViewModel.PlayerViewModel",
            InstancePath = "DuelSceneViewModel",
            ArgumentValues = ["IsTargetable", "IsTargeted", "Active", "", null]
          },
          new()
          {
            TypeFullName = "Shiny.Play.Duel.ViewModel.OptionButton",
            InstancePath = "PromptBoxVM.DuelSceneViewModel",
            ArgumentValues = ["Enabled", "Visible", "", null]
          }
        ]
      }, HarmonyPatchPosition.Postfix);
  }

  // A mode change can reveal a replacement scene. Retain the callback's scene
  // reference for subscription binding, and read interaction values from its capture.
  private static EventHookProxy<dynamic, HookContext> s_modeChanged = new(
    SceneType, "InteractionStateModeChanged",
    new CapturedEventHook((context, instance, _) =>
      instance is null ? null : (instance, context)),
    new RemoteHookCapture
    {
      Batch = CreateCaptureBatch(ObjectOrRemoteAddress.Null).ToRequest(),
      Inputs = [-1],
      Scope = CaptureScope(),
      CaptureOnExit = true
    }, HarmonyPatchPosition.Postfix);

  // These hooks enclose native interaction updates. Nested calls for the same
  // game share a scope, which captures the resulting values on the outermost return.
  private static readonly EventHookProxy<string, HookContext>[] s_captureCompleted =
  [
    CreateSceneHook(PromptBoxType, "Execute", "DuelSceneViewModel"),
    CreateSceneHook(SceneType, "LeftClickDuringSelectTargets"),
    CreateSceneHook(PromptBoxType, "CurrentTargets_CollectionChanged", "DuelSceneViewModel"),
    CreateSceneHook(PromptBoxType, "AdvanceToNextTargetListIfAppropriate", "DuelSceneViewModel"),
    CreateSceneHook(PromptBoxType, "ProcessUpdate", "DuelSceneViewModel"),
    CreateSceneHook(SceneType, "UpdateViews"),
    CreateSceneHook(SceneType, "UpdateZoneViews")
  ];

  private static RemoteCaptureScope CaptureScope(string? ownerPath = null) => new()
  {
    Group = CaptureGroup,
    OwnerInput = 0,
    OwnerPath = ownerPath is null ? "Game" : ownerPath + ".Game"
  };

  private static EventHookProxy<string, HookContext> CreateSceneHook(
    string type, string method, string? scenePath = null) => new(type, method,
      new CapturedEventHook((context, _, _) => (method, context)),
      new RemoteHookCapture
      {
        Batch = CreateCaptureBatch(ObjectOrRemoteAddress.Null, scenePath).ToRequest(),
        Inputs = [-1],
        IncludeParameters = false,
        Scope = CaptureScope(scenePath)
      }, HarmonyPatchPosition.Postfix);

  //
  // Scene subscriptions and captured state
  //

  private sealed record CollectionSubscription(
    string Name,
    EventProxy Changes);

  private sealed record CaptureReadiness(Guid StreamId, bool Ready, long? FirstSequence);

  // Subscription changes, scene replacement, and disposal share this lock.
  // Acquire it before _captureLock whenever an operation needs both locks.
  private readonly object _subscriptionLock = new();
  private readonly object _captureLock = new();
  private Game? _game;
  private int _gameId;
  private DynamicRemoteObject? _scene;
  private RemoteBatch? _notificationCapture;
  private IPropertyChangeSubscription? _textChanges;
  private EventProxy? _viewLoaded;
  private CancellationTokenSource? _initialCapture;
  private ulong? _viewToken;
  private readonly Dictionary<ulong, CollectionSubscription> _collections = new();
  private InteractionStateSnapshot _captured = InteractionStateSnapshot.Empty;
  private readonly Queue<InteractionStateSnapshot> _pendingChanges = new();
  private readonly RemoteCaptureQueue<HookContext> _captureQueue = new();
  private bool _notifying;
  private volatile bool _initialized;
  private bool _subscribed;
  private DiverCommunicator? _captureConnection;
  private CaptureReadiness? _captureReadiness;
  private bool _disposed;

  /// <summary>
  /// Raised when mode, text, controls, card actions, or selection flags change.
  /// The mode can remain unchanged when another captured value changes.
  /// </summary>
  public event Action<InteractionMode>? Changed;

  // Pass the snapshot to the processor so callback delays cannot change the
  // values associated with this notification.
  internal event Action<InteractionStateSnapshot>? StateChanged;

  /// <summary>
  /// Gets the latest captured interaction state without a remote call.
  /// </summary>
  public InteractionStateSnapshot Snapshot => Volatile.Read(ref _captured);

  // Require interaction capture only for notifications from the current stream
  // after UI readiness. Earlier notifications may precede construction of the view.
  internal bool RequiresCapture(RemoteCaptureSequence sequence)
  {
    var readiness = Volatile.Read(ref _captureReadiness);
    return readiness is { Ready: true, FirstSequence: long first }
      && sequence.StreamId == readiness.StreamId
      && sequence.NotificationSequence >= first;
  }

  /// <summary>
  /// Gets the last captured mode for this game's scene, or
  /// <see cref="InteractionMode.Undefined"/> before a mode has been captured.
  /// </summary>
  public InteractionMode Mode => Current.Mode;

  /// <summary>
  /// Gets the last captured text displayed in the prompt box.
  /// </summary>
  /// <remarks>
  /// During target selection this is the targeting instruction (for example,
  /// "Choose any target..."). This displayed instruction can differ from the
  /// correlated native <see cref="Game.Prompt"/> text during local selection.
  /// It is null before text has been captured or when the captured text is null.
  /// </remarks>
  public string? PromptBoxText => Current.PromptBoxText;

  /// <summary>
  /// Gets the mode and displayed text from the same snapshot without a remote call.
  /// </summary>
  public (InteractionMode Mode, string? PromptBoxText) Current
  {
    get
    {
      var snapshot = Snapshot;
      return (snapshot.Mode, snapshot.PromptBoxText);
    }
  }

  /// <summary>
  /// Subscribes to native interaction updates for the game.
  /// </summary>
  /// <remarks>
  /// Initial capture waits asynchronously for UI readiness. Repeated calls can
  /// discover a scene that was unavailable during an earlier attempt.
  /// </remarks>
  public void Initialize(Game game)
  {
    ArgumentNullException.ThrowIfNull(game);
    if (_initialized)
      return;

    lock (_subscriptionLock)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(InteractionModeTracker));
      if (_initialized)
        return;

      try
      {
        if (!_subscribed)
        {
          _game = game;
          _gameId = game.Id;
          // Mark subscription ownership before adding handlers. A proxy can retain
          // a delegate before throwing, so failure cleanup must remove the handlers.
          _subscribed = true;
          _captureConnection = RemoteClient.@client.Communicator;
          _captureConnection.CaptureCompleted += OnNativeCaptureCompleted;
          s_modeChanged += OnModeChanged;
          s_propertiesChanged += OnCaptureCompleted;
          for (int index = 0; index < s_captureCompleted.Length; ++index)
            s_captureCompleted[index] += OnCaptureCompleted;
        }

        // Game discovery can precede scene construction. Leave initialization
        // incomplete when no scene is available so a later call can retry.
        var scene = DuelSceneViewModel.ForGame(_game!);
        if (scene is not null)
          BindSceneLocked((DynamicRemoteObject)DLRWrapper.Unbind(scene));
      }
      catch
      {
        Dispose();
        throw;
      }

      if (_scene is not null)
        _initialized = true;
    }
  }

  private void OnModeChanged(dynamic scene, HookContext context)
  {
    try
    {
      lock (_subscriptionLock)
      {
        lock (_captureLock)
        {
          if (_disposed) return;
          bool currentStream = context.CaptureSequence is { } sequence && _captureQueue.Matches(sequence);
          if (!currentStream && context.Capture?.Values["gameId"].GetScalar<int>() != _gameId)
            return;
        }
        BindSceneLocked((DynamicRemoteObject)scene);
      }
      // Binding may start an asynchronous initial capture. Consume any capture
      // carried by this notification through the same ordered delivery path.
      if (context.CaptureSequence is not null) CaptureObservedState(context);
    }
    catch (Exception error)
    {
      Log.Error(error, "Failed to bind interaction scene for game {0}.", _gameId);
    }
  }

  private void OnCaptureCompleted(string _, HookContext context) =>
    CaptureObservedState(context);

  // Other SDK clients can contribute captures to the shared native stream.
  // Observe every completion to avoid gaps in its sequence, while the individual
  // hook and property handlers maintain this tracker's scene subscriptions.
  private void OnNativeCaptureCompleted(HookContext context) =>
    CaptureObservedState(context);

  //
  // Scene attachment and subscription maintenance
  //

  private void BindSceneLocked(DynamicRemoteObject scene)
  {
    if (_scene?.__ro.RemoteToken == scene.__ro.RemoteToken)
      return;

    DynamicRemoteObject promptBox = (DynamicRemoteObject)((dynamic)scene).PromptBox;
    var capture = CreateCaptureBatch(scene);
    var subscription = PropertyChangeRouter.Shared.Subscribe(promptBox, capture, CaptureScope(), OnTextChanged, "Text");
    try
    {
      var baseline = subscription.CaptureSequence
        ?? throw new InvalidOperationException("Native interaction subscription did not supply its stream baseline.");
      lock (_captureLock)
      {
        _captureQueue.Reset(baseline);
        _scene = scene;
        Volatile.Write(ref _captureReadiness, new(baseline.StreamId, false, 0));
      }
    }
    catch
    {
      subscription.Dispose();
      throw;
    }

    if (_textChanges is not null)
    {
      _textChanges.PropertyChanged -= OnTextChanged;
      _textChanges.Dispose();
    }

    ClearCollectionSubscriptionsLocked();
    _viewLoaded?.Clear();
    _initialCapture?.Cancel();
    _initialCapture = null;
    _viewToken = null;

    _notificationCapture = capture;
    lock (_captureLock)
      _textChanges = subscription;
    _viewLoaded = new EventProxy(scene, "DuelViewLoaded");
    _viewLoaded += (Action<dynamic, dynamic>)OnViewLoaded;
    ObserveCollectionLocked((DynamicRemoteObject)((dynamic)promptBox).StandardButtons, "standard");
    ObserveCollectionLocked((DynamicRemoteObject)((dynamic)promptBox).ManaButtons, "mana");
    ObserveCollectionLocked((DynamicRemoteObject)((dynamic)scene).Players, "players");
    // Subscribe to DuelViewLoaded before checking DuelView. This also handles
    // a view that finished loading before the event handler was attached.
    BeginInitialCaptureLocked();
  }

  private void BeginInitialCaptureLocked()
  {
    DynamicRemoteObject? view = (DynamicRemoteObject?)((dynamic)_scene!).DuelView;
    if (view is null)
      return;
    ulong token = view.__ro.RemoteToken;
    if (_viewToken == token && (_initialCapture is not null || _captureReadiness is { Ready: true }))
      return;

    _initialCapture?.Cancel();
    _viewToken = token;
    lock (_captureLock)
    {
      if (_captureReadiness is { } readiness)
        Volatile.Write(ref _captureReadiness, readiness with { Ready = false });
    }
    var cancellation = new CancellationTokenSource();
    _initialCapture = cancellation;
    _ = CaptureInitialStateAsync(view, cancellation);
  }

  private async Task CaptureInitialStateAsync(DynamicRemoteObject view,
    CancellationTokenSource cancellation)
  {
    try
    {
      await UIDispatcher.WaitForReadyAsync(view, cancellation.Token).ConfigureAwait(false);
      IPropertyChangeSubscription? subscription;
      lock (_subscriptionLock)
      {
        if (_disposed || cancellation.IsCancellationRequested || _initialCapture != cancellation)
          return;
        subscription = PrepareInitialCaptureLocked();
      }
      if (subscription is not null)
        await subscription.CaptureAsync(forceUIThread: true).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
      // Scene replacement or disposal cancels readiness for the previous view.
    }
    catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
    {
      // The old scene's subscription may detach while its request is pending.
    }
    catch (Exception error)
    {
      if (!cancellation.IsCancellationRequested)
        Log.Error(error, "Failed to capture initial client UI state for game {0}.", _gameId);
    }
    finally
    {
      lock (_subscriptionLock)
        if (_initialCapture == cancellation) _initialCapture = null;
      cancellation.Dispose();
    }
  }

  private void ObserveCollectionLocked(DynamicRemoteObject collection, string name)
  {
    var changes = new EventProxy(collection, "CollectionChanged");
    // Retain the proxy before remote registration so initialization failure can
    // clean up a subscription that was only partially established.
    ulong token = collection.__ro.RemoteToken;
    _collections.Add(token, new(name, changes));
    changes.Subscribe(context => CaptureObservedState(context),
      new RemoteEventCapture { Batch = _notificationCapture!.ToRequest(), Scope = CaptureScope() });
  }

  private void ObserveCardCollectionsLocked()
  {
    // Revisit the zone dictionaries at readiness and on player collection changes
    // to attach subscriptions to each player's current card collections.
    var current = new HashSet<ulong>();
    void ObserveZone(dynamic zone)
    {
      var cards = (DynamicRemoteObject)zone.Cards;
      ulong token = cards.__ro.RemoteToken;
      current.Add(token);
      if (_collections.ContainsKey(token))
        return;
      ObserveCollectionLocked(cards, "cards");
    }

    foreach (dynamic zone in (System.Collections.IEnumerable)((dynamic)_scene!).m_sharedZones.Values)
      ObserveZone(zone);
    foreach (dynamic zones in (System.Collections.IEnumerable)((dynamic)_scene!).m_perPlayerZones.Values)
      foreach (dynamic zone in (System.Collections.IEnumerable)zones.Values)
        ObserveZone(zone);

    foreach (var (token, collection) in _collections.Where(entry =>
      entry.Value.Name == "cards" && !current.Contains(entry.Key)).ToArray())
    {
      collection.Changes.Clear();
      _collections.Remove(token);
    }
  }

  //
  // Applying native captures
  //

  private void OnViewLoaded(dynamic sender, dynamic args)
  {
    lock (_subscriptionLock)
    {
      if (_disposed || _scene?.__ro.RemoteToken != ((DynamicRemoteObject)sender).__ro.RemoteToken)
        return;
      BeginInitialCaptureLocked();
    }
  }

  private void CaptureObservedState(HookContext context)
  {
    InteractionStateSnapshot? changedState = null;
    var changedCollections = new HashSet<ulong>();
    try
    {
      lock (_captureLock)
      {
        if (_disposed || _scene is null)
          return;

        var sequence = context.CaptureSequence
          ?? throw new InvalidOperationException("Native interaction notification has no completion sequence.");
        if (!_captureQueue.Matches(sequence))
          return; // Another game's stream or a callback from the previous scene attachment.
        if (sequence.Sequence < _captureQueue.NextSequence)
          return; // Already consumed by stream observation or a per-source callback.

        // Callbacks may arrive out of order. The queue holds later captures until
        // every preceding sequence has arrived, then releases them in native order.
        var completed = _captureQueue.Add(sequence, context);
        foreach (var ready in completed)
        {
          try
          {
            changedState = AcceptCaptureLocked(ready) ?? changedState;
          }
          catch (Exception error)
          {
            // This sequence has already left the queue. Log its failure and
            // continue applying the remaining captures released with it.
            Log.Error(error, "Failed to consume native interaction capture for game {0}, sequence {1}.",
              _gameId, ready.CaptureSequence!.Sequence);
          }

          // A gameplay boundary carries the same collection-change metadata as
          // its interaction capture. Refresh subscriptions for the interaction
          // entry once, without repeating the work for the boundary.
          var completedSequence = ready.CaptureSequence!;
          if (!completedSequence.IsStateBoundary &&
            completedSequence.EventSources.TryGetValue("CollectionChanged", out var sources))
            changedCollections.UnionWith(sources);
        }
      }
      NotifyChanged(changedState);

      if (changedCollections.Count == 0)
        return;

      // Player replacement can introduce new zone collections. Update those fixed
      // collection subscriptions without enumerating their cards or controls.
      lock (_subscriptionLock)
      {
        lock (_captureLock)
        {
          if (_disposed || _captureReadiness is not { Ready: true } || !_captureQueue.Matches(context.CaptureSequence!))
            return;
        }

        changedCollections.RemoveWhere(token => !_collections.ContainsKey(token));
        if (changedCollections.Count == 0)
          return;

        if (changedCollections.Any(token => _collections[token].Name == "players"))
          ObserveCardCollectionsLocked();
      }
    }
    catch (Exception error)
    {
      Log.Error(error, "Failed to consume client UI capture for game {0}.", _gameId);
    }
  }

  private void ClearCollectionSubscriptionsLocked()
  {
    foreach (var entry in _collections.Values)
      entry.Changes.Clear();
    _collections.Clear();
  }

  private void OnTextChanged(object? sender, RemotePropertyChangedEventArgs args)
  {
    lock (_captureLock)
      if (_disposed || !ReferenceEquals(sender, _textChanges))
        return;
    CaptureObservedState(args.CaptureContext!);
  }

  private IPropertyChangeSubscription? PrepareInitialCaptureLocked()
  {
    lock (_captureLock)
    {
      var readiness = _captureReadiness! with
      { Ready = true, FirstSequence = null };
      Volatile.Write(ref _captureReadiness, readiness);
    }

    ObserveCardCollectionsLocked();

    return _textChanges;
  }

  //
  // Capture construction and snapshot decoding
  //

  /// <summary>
  /// Builds the reflection operations used to capture interaction state.
  /// </summary>
  /// <remarks>
  /// Building the batch performs no remote calls. The Diver executes notification
  /// captures on the native thread before delivering their callbacks.
  /// </remarks>
  internal static RemoteBatch CreateCaptureBatch(object sceneInput, string? scenePath = null)
  {
    var batch = new RemoteBatch();
    var scene = batch.Input(sceneInput);
    if (scenePath is not null) scene = batch.Read(scene, scenePath);

    // Retain the native prompt identity with the displayed text and mode so the
    // processor can correlate this interaction capture with its server snapshot.
    batch.Output("gameId", batch.Read(scene, "GameId"));
    batch.Output("scene", scene, RemoteBatchProjection.Object().WithLogicalAddress());
    batch.Output("mode", batch.Read(scene, "InteractionState.Mode"));
    batch.Output("text", batch.Read(scene, "PromptBox.Text"));
    batch.Output("prompt", batch.Read(scene, "Game.Prompt"),
      RemoteBatchProjection.Object(
        ("Timestamp",      "Timestamp",      RemoteBatchProjection.Value()),
        ("PromptedPlayer", "PromptedPlayer", RemoteBatchProjection.Value()),
        ("Text",           "Text",           RemoteBatchProjection.Value())));

    var scalar = RemoteBatchProjection.Value();
    var buttonProjection = RemoteBatchProjection.Object(
      ("name",    "Name",    scalar),
      ("enabled", "Enabled", scalar),
      ("visible", "Visible", scalar),
      ("action",  "Action",  RemoteBatchProjection.Object()));
    batch.Output("standard", batch.Read(scene, "PromptBox.StandardButtons"),
      RemoteBatchProjection.Sequence(buttonProjection));

    var manaProjection = RemoteBatchProjection.Object(
      ("name",    "Name",    scalar),
      ("enabled", "Enabled", scalar),
      ("visible", "Visible", scalar),
      ("color",   "Color",   scalar),
      ("action",  "Action",  RemoteBatchProjection.Object()));
    batch.Output("mana", batch.Read(scene, "PromptBox.ManaButtons"),
      RemoteBatchProjection.Sequence(manaProjection));

    batch.Output("players", batch.Read(scene, "Players"),
      RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
        ("index",      "GamePlayer.Id", scalar),
        ("targetable", "IsTargetable",  scalar),
        ("targeted",   "IsTargeted",    scalar),
        ("active",     "Active",        scalar))));

    // Read cards from the zone collections used by MTGO's targetability updates.
    // Capture their actions and selection flags with the prompt controls so local
    // decoding does not need to reread changing card views.
    var card = RemoteBatchProjection.Object(
      ("id",       "GameCard.Id",       scalar),
      ("sourceId", "GameCard.SourceId", scalar),
      ("card",     "GameCard", RemoteBatchProjection.Object(
        ("Id",            "Id",       scalar),
        ("SourceId",      "SourceId", scalar),
        ("Name",          "Name",     scalar),
        ("GameInterface", "GameInterface", RemoteBatchProjection.Object(
          ("Id", "Id", scalar)))).WithLogicalAddress()),
      ("hasActions", "HasActions",   scalar),
      ("selectable", "IsSelectable", scalar),
      ("targetable", "IsTargetable", scalar),
      ("targeted",   "IsTargeted",   scalar),
      ("actions",    "Actions",
        RemoteBatchProjection.Sequence(GameAction.CreateCaptureProjection())));

    // Resolve each card's primary view before projecting it. Following this link
    // inside the projection can revisit an object already being serialized, which
    // the serializer reports as a cycle.
    void CaptureZone(RemoteBatchScope zone) => zone.ForEach(zone.Read("Cards"), row =>
      row.Output("card", row.Read("GameCard.Tag.CardViewModel"), card));
    batch.ForEach(batch.Read(scene, "m_sharedZones.Values"), CaptureZone);
    batch.ForEach(batch.Read(scene, "m_perPlayerZones.Values"), zones =>
      zones.ForEach(zones.Read("Values"), CaptureZone));
    return batch;
  }

  private InteractionStateSnapshot? AcceptCaptureLocked(HookContext context)
  {
    if (_captureReadiness is not { Ready: true } readiness)
      return null;

    var sequence = context.CaptureSequence
      ?? throw new InvalidOperationException("Native interaction notification has no completion sequence.");

    // The queue also orders gameplay boundary captures. GameProcessor handles
    // their server data, so they advance the sequence without updating UI values.
    if (sequence.IsStateBoundary)
      return null;

    if (readiness.FirstSequence is null)
    {
      if (!sequence.Requested || sequence.SourceToken != _textChanges?.Token)
        return null;
      readiness = readiness with { FirstSequence = sequence.Sequence };
      Volatile.Write(ref _captureReadiness, readiness);
    }
    if (sequence.Sequence < readiness.FirstSequence)
      return null;

    if (context.CaptureError is { } error)
      throw new InvalidOperationException($"Native interaction capture failed: {error.Message}");
    var result = context.Capture
      ?? throw new InvalidOperationException("Native interaction notification has no detached capture.");
    if (result.Values["gameId"].GetScalar<int>() != _gameId
      || result.Values["scene"].LogicalAddress != _scene!.__ro.RemoteToken)
      return null;

    return ApplyCaptureLocked(result, sequence);
  }

  private InteractionStateSnapshot? ApplyCaptureLocked(
    RemoteBatchResult result, RemoteCaptureSequence sequence)
  {
    var mode = (InteractionMode)result.Values["mode"].GetScalar<int>();
    if (!Enum.IsDefined(mode))
      mode = InteractionMode.Undefined;

    var textValue = result.Values["text"];
    string? text = textValue.Kind == RemoteBatchValueKind.Null
      ? null
      : textValue.GetScalar<string>();
    var nativePrompt = result.Values["prompt"];
    int? promptNonce = nativePrompt.Kind == RemoteBatchValueKind.Null ? null
      : new GamePrompt(RemoteBatchObject.FromValue(nativePrompt)!).Nonce;
    var buttons = ReadButtons(result.Values["standard"], PromptButtonGroup.Standard)
      .Concat(ReadButtons(result.Values["mana"], PromptButtonGroup.Mana)).ToArray();
    var players = result.Values["players"].Items.Select(value =>
      new PlayerSelectable(
        value.Members["index"].GetScalar<int>(),
        value.Members["targetable"].GetScalar<bool>(),
        value.Members["targeted"].GetScalar<bool>(),
        value.Members["active"].GetScalar<bool>()))
      .OrderBy(player => player.PlayerIndex).ToArray();
    var cards = new SortedDictionary<ulong, CardInteractionSnapshot>();
    void ReadRows(IEnumerable<RemoteBatchRow> rows)
    {
      foreach (var row in rows)
      {
        if (row.Error is { } error)
          throw new InvalidOperationException(
            $"Card capture failed at operation {error.OperationId}, row {row.SourceIndex}, {error.OutputPath}: {error.Message}");
        if (row.Values.TryGetValue("card", out var value) && value.Kind != RemoteBatchValueKind.Null)
        {
          var card = new CardInteractionSnapshot((RemoteBatchObject)RemoteBatchObject.FromValue(value)!);
          // Several zones may expose the same primary card view. Merge entries
          // by logical address only when their captured values agree.
          if (cards.TryGetValue(card.LogicalAddress, out var existing) && !existing.Equals(card))
            throw new InvalidOperationException(
              $"Conflicting captured views for logical address {card.LogicalAddress}" +
              $" (Id: {card.CardId}, SourceId: {card.SourceId}, Name: {card.Name}, Row: {row.SourceIndex}).");
          cards[card.LogicalAddress] = card;
        }
        ReadRows(row.Rows);
      }
    }
    ReadRows(result.Rows);
    var previous = Volatile.Read(ref _captured);
    if (previous.PromptNonce == promptNonce && previous.StateRevision == sequence.StateRevision
      && previous.CaptureStream == sequence.StreamId
      && previous.Mode == mode
      && previous.PromptBoxText == text && previous.Buttons.SequenceEqual(buttons)
      && previous.Players.SequenceEqual(players) && previous.Cards.SequenceEqual(cards))
      return null;

    var captured = new InteractionStateSnapshot(promptNonce, mode, text,
      Array.AsReadOnly(buttons), Array.AsReadOnly(players),
      new ReadOnlyDictionary<ulong, CardInteractionSnapshot>(cards), previous.Version + 1)
    {
      CaptureStream = sequence.StreamId,
      StateRevision = sequence.StateRevision,
      CaptureSequence = sequence.Sequence
    };
    Volatile.Write(ref _captured, captured);
    // Enqueue while holding _captureLock so notifications follow the order in
    // which snapshots were applied, even when callers notify concurrently.
    _pendingChanges.Enqueue(captured);
    return captured;
  }

  private static IEnumerable<PromptButton> ReadButtons(
    RemoteBatchValue sequence, PromptButtonGroup group)
  {
    for (int index = 0; index < sequence.Items.Count; index++)
    {
      var value = sequence.Items[index];
      if (!value.Members["visible"].GetScalar<bool>())
        continue;
      var name = value.Members["name"];
      yield return new PromptButton(
        name.Kind == RemoteBatchValueKind.Null ? null : name.GetScalar<string>(),
        group == PromptButtonGroup.Mana ? value.Members["color"].EnumName : null,
        value.Members["enabled"].GetScalar<bool>(), new(group, index),
        value.Type!.FullName.Split('.', '+').Last(),
        value.Members["action"].Kind != RemoteBatchValueKind.Null);
    }
  }

  //
  // Change notification and disposal
  //

  private void NotifyChanged(InteractionStateSnapshot? changedState)
  {
    if (changedState is null)
      return;
    lock (_captureLock)
    {
      if (_disposed || _notifying || _pendingChanges.Count == 0)
        return;
      _notifying = true;
    }

    // Invoke subscribers outside _captureLock because processor callbacks can
    // acquire other locks. _notifying keeps one caller draining the queue in order.
    while (true)
    {
      InteractionStateSnapshot captured;
      lock (_captureLock)
      {
        if (_disposed || _pendingChanges.Count == 0)
        {
          _notifying = false;
          return;
        }
        captured = _pendingChanges.Dequeue();
      }

      NotifyHandlers(StateChanged, captured);
      NotifyHandlers(Changed, captured.Mode);
    }
  }

  // Isolate subscriber failures so one handler cannot prevent the remaining
  // handlers from receiving the captured update.
  private void NotifyHandlers<T>(Action<T>? handlers, T value)
  {
    if (handlers is null)
      return;
    foreach (Action<T> handler in handlers.GetInvocationList())
    {
      try
      {
        handler(value);
      }
      catch (Exception error)
      {
        Log.Error(error, "Interaction state handler failed for game {0}.", _gameId);
      }
    }
  }

  public void Dispose()
  {
    bool subscribed;
    Game? game;
    IPropertyChangeSubscription? textChanges;

    lock (_subscriptionLock)
    {
      lock (_captureLock)
      {
        if (_disposed)
          return;

        _disposed = true;
        Volatile.Write(ref _captureReadiness, null);
        _pendingChanges.Clear();
        _initialized = false;
        subscribed = _subscribed;
        game = _game;
        textChanges = _textChanges;
        _subscribed = false;
        _game = null;
        _gameId = 0;
        _scene = null;
        _textChanges = null;
        var previous = Volatile.Read(ref _captured);
        Volatile.Write(ref _captured, previous with
        {
          Buttons = Array.Empty<PromptButton>(),
          Players = Array.Empty<PlayerSelectable>(),
          Cards = InteractionStateSnapshot.Empty.Cards
        });
      }

      if (_captureConnection is not null)
        _captureConnection.CaptureCompleted -= OnNativeCaptureCompleted;
      _captureConnection = null;
      _notificationCapture = null;
      _viewLoaded?.Clear();
      _viewLoaded = null;
      _initialCapture?.Cancel();
      _initialCapture = null;
      _viewToken = null;
      ClearCollectionSubscriptionsLocked();
    }

    if (subscribed)
    {
      s_modeChanged -= OnModeChanged;
      s_propertiesChanged -= OnCaptureCompleted;
      for (int index = 0; index < s_captureCompleted.Length; ++index)
        s_captureCompleted[index] -= OnCaptureCompleted;
    }

    if (textChanges is not null)
    {
      textChanges.PropertyChanged -= OnTextChanged;
      textChanges.Dispose();
    }

    GC.KeepAlive(game);
  }
}
