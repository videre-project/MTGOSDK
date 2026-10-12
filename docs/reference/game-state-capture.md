# Game state capture

The gameplay postfix captures message properties and completed native card values
with one `RemoteBatch`, alongside the correlated prompt. `GameCardCapture` owns
native member identities and acceptance policy in the game layer. The Diver
executes generic reflection and serialization.

`GameCardPartial` and the internal `GameCardCapture` helper share
`Processors/Partials/GameCardPartial.cs`, keeping the capture shape and local
adapter together. Their existing namespaces and type identities are preserved.

## Processor organization

Processor-owned state models live in `MTGOSDK.API.Play.Games.Processors.Snapshots`.
Each public snapshot and selection model has its own file. These types describe
captured processor results; the game-domain types remain in `Games.Types`.

`Intermediate/` contains `IIntermediateTickProcessor`, `InteractionModeTracker`,
`CardActionRegistry`, and the combat, zone-change, property-change, and
revealed-zone processors. These use the
`MTGOSDK.API.Play.Games.Processors.Intermediate` namespace.
Event payloads and `ProcessorEvent<T>` live together in `Processors.Events`.
`PlayerSelectable` is an independent snapshot type. `PromptButton` lives alongside
`PromptControl` in `MTGOSDK.API.Interface.Controls`.

`CardActionRegistry` exposes captured actions, selection flags, and prompt
controls. Live scene discovery and card positioning belong to
`MTGOSDK.API.Interface.ViewModels.DuelSceneViewModel`. Consumers implement
prompt-button execution using the captured control identity.
`DuelSceneViewModel.ForGame(game)` uses MTGO's
`PlaySceneViewModel.GetViewModelForJoinable(game.Match)` lookup and reads the
scene retained by that match's `GameDetailsViewModel`. It verifies the scene's
game ID and disposal state. This works when connecting to an already-open match
without enumerating cards or the managed heap. Delayed spectator scenes are
checked when the window's current scene belongs to another game or is absent.
`GetCardPosition` returns
a `CardViewModel.ScreenBounds`, while `GetCardPositions` returns a dictionary of
`CardViewModel.Position` values containing screen bounds and drawing order.
`CardViewModel` owns geometry capture and caches the method/type descriptors once.
The value types are nested in the same class; the duel-scene methods delegate to it. One UI-thread batch captures
the current card views, visibility, four transformed corners, shared-panel
identity, and each view container's Z index and child order. Failed rows and invisible views are omitted.

Panel indices identify shared panels within that call. Drawing order can be
compared only for cards sharing a panel index in the same result. The bounds
include rotation, scaling, and ancestor transforms, but a view can move or be
covered after capture. AlphaMTG validates captured controls against the live
prompt buttons and invokes their native `OnClick()` method. That execution code
lives in AlphaMTG's `GameController`.

## Data flow

1. After native `HandleGamePlayStatus` completes, the existing postfix batch
   reads the message's `ThingElements` in source order.
2. Each row serializes its integer, string, and recursive child property
   dictionaries, plus `FromZone` and native type text.
3. `ResolveDigitalThingAsGameCard(THINGNUMBER)` looks up the already-resolved
   native card. The batch projects its associations, scalar members, counters,
   abilities, and definition. Player rows retain their message properties and
   have a successful null card value.
4. The batch also captures the ordered state elements, priority player, roster
   names, and property hashes. The callback transports these values with the
   card rows and correlated prompt.
5. The SDK uses the captured hashes to select changed card and player rows, then
   parses the detached state elements locally. Missing, repeated, or failed
   selected rows reject the capture. Player User references use the game
   reference cache rather than repeated roster navigation.

Capturing values does not construct native cards or call their property resolver.
Constructing a temporary card against the live game is unsafe: native resolution
can recursively create other faces and insert them into live zones, causing
collection notifications and WPF work. A `SPLITPARENT_ID` marker on the top-level
copy does not protect those recursively constructed cards.

The hook runs on its originating native thread without a dispatcher request or
nested IPC. Each message projects all of its rows; the SDK subsequently reuses
unchanged local adapters. Projection runs before callback delivery at the native completion
point. Rows and intermediate objects are not
pinned. Hashing runs inside this batch. Resolving an uncached live player or zone
reference remains a separate operation.

## Dispatch in the capture path

Gameplay card/player capture and live type-line reads do not force UI dispatch.
The action registry reads notification-driven captured actions without IPC.

Interaction captures include mode, displayed text, native prompt identity, controls,
player flags, card actions, and card selection flags. Their batch executes on the
originating native thread, without a dispatcher request or nested IPC. The SDK
consumes detached values and performs no later `Game.Prompt` read.

### Native interaction scopes

MTGO changes eligibility, displayed text, and controls in separate statements
within the same method. Property notifications can therefore describe intermediate
states, even when all getters are read synchronously. A matching server nonce
identifies the originating prompt but does not establish local UI completion.

`InteractionModeTracker` enrolls these enclosing native calls in a capture group:

- `PromptBoxViewModel.Execute`, `ProcessUpdate`,
  `CurrentTargets_CollectionChanged`, and `AdvanceToNextTargetListIfAppropriate`.
- `DuelSceneViewModel.InteractionStateModeChanged`,
  `LeftClickDuringSelectTargets`, `UpdateViews`, and `UpdateZoneViews`.

Interaction cards are keyed by their native card object's Diver logical address,
not MTGO `Id` or `SourceId`. Distinct objects can share those values, including
`-1`; repeated primary-view representations of the same object deduplicate by
logical address. Gameplay and action-card projections carry that same address so
registry lookups accept SDK card objects or logical addresses without ID/source
fallback. Native IDs remain captured data for server-state and zone history.
Capturing addresses acquires no pins, and retained snapshots contain detached
values rather than live handles.

The scope owner is the actual native game object. Scene hooks reach it through
`Game`; prompt-box hooks use `DuelSceneViewModel.Game`. `HandleGamePlayStatus`
enrolls the same group as a state boundary. Names, paths, and capture projections
are SDK request data; the Diver has no game-specific scope logic.

A paired prefix/finalizer creates invocation-local scope state inside the Diver.
Nested enrolled calls share the same group and owner only on their calling thread.
Participating notifications mark the scope changed without executing the full
projection. For ordinary interaction scopes, the successful outermost completion
executes one batch after text and controls have finished changing. A gameplay
boundary advances the owner's `StateRevision` on entry and defers the first
participating notification or nested interaction-hook projection until completion.
It queues that completed UI capture before its server capture, with the same
revision and an explicit `NotificationSequence` link. Additional boundary
registrations retain their own server callbacks. Standalone local captures inherit
the latest state revision. Unchanged interaction calls produce no entry; the mode
handler marks a mode transition changed explicitly. Gameplay boundaries always
produce their server capture.
Failures close the scope and carry an explicit error in its completion stream.
There is no rollback of native effects and no grouping across asynchronous work.

Each owner/group stream has a native identity and contiguous completion sequence.
The initial text subscription returns its sequence baseline. The SDK buffers later
entries behind a gap and applies contiguous completions in native order, including
failures. `DiverCommunicator.CaptureCompleted` exposes stream entries before ordinary
token routing, including callbacks registered by another client sharing the Diver.
Each tracker filters by its registered stream identity; another client's event token
cannot create an unobserved sequence gap. Ordinary event callbacks keep their
subscription filters. Grouped and standalone captures retain existing subscription
source tokens by event name in `EventSources`; this creates no new remote handles.
A new scene attachment establishes its baseline explicitly. Standalone
notifications also detach their values synchronously and use this same stream.
`RemoteCaptureSequence.Scoped` identifies completions grouped by an enrolled
invocation. Registered boundaries do not imply that every native mutation
executes within an enrolled call.

Card, player, and option-button property changes use one shared hook on native
`NotifyPropertyChangedImplementation.OnPropertyChanged(string)`. Caller-supplied
filters select the relevant native types and property names before projection or
callback transport. Each matching object resolves its owning scene, which must
still have an enrolled capture subscription. New card views therefore require no
individual property subscription or collection scan.

The relevant setters in the current native decompilation use this string
notification path. The separate expression-based notification method raises the
event directly, but has no relevant callers in these view-model paths. Collection
changes retain fixed subscriptions on standard buttons, mana buttons, players,
and zone card collections. Player replacement discovers its zone collections;
ordinary card and button collection changes do not enumerate or bind their items.
Completed projections supply card values without merging intermediate revisions.

Scoped property subscriptions install their callback before activating the native
filter. Registration starts with an empty filter; setting the installed union
activates notifications after local routing and the stream baseline are available.
This adds one setup request per new scoped property subscription, outside capture.
The mode hook also retains scene discovery before the first subscription: its
unsequenced announcement binds the owner, and readiness supplies the initial state.

For subscriptions with capture, the router sends the union of their property
filters to the Diver. The native handler checks that filter before executing the
batch or queueing delivery. Changing a filter replaces the native union without
detaching the handler; local delivery applies each subscription's own filter.
Null or empty notification names mean that all properties changed. An empty
subscription filter observes none. Relevant capture failures remain explicit.

Initial attachment requests one capture from the retained property subscription
after UI readiness. The Diver dispatches that request to the UI thread, detaches
its values, and queues it in the same native completion stream as notifications.
The requested callback identifies its source subscription and native sequence
position. The tracker starts there through the ordinary ordered callback path,
independently of when the request acknowledgement arrives, and preserves every
later completion. No unsequenced read can overtake a delayed notification.
Captures from replaced scenes advance the stream without replacing the current
scene's values.

Fixed zone collection subscriptions are attached at readiness and refreshed when
players change. These setup reads do not supply the prompt identity or UI values
of a delivered notification.

Screen-position reads access WPF elements and call `PointToScreen`. Native
control clicks and AlphaMTG's selection/input operations also retain UI dispatch.
Those execution paths are separate from state export.

## Native members without an SDK scalar table

The member projection uses:

```csharp
RemoteBatchProjection.ObjectScalars(
    new RemoteBatchType(
        "WotC.MtGO.Client.Model.Play.IGameCard",
        "WotC.MtGO.Client.Model.Reference"),
    // Complex members have explicit projections.
    ("Owner", "Owner.Id", RemoteBatchProjection.Value()),
    ("Counters", "Counters", RemoteBatchProjection.Dictionary(
        RemoteBatchProjection.Value(), RemoteBatchProjection.Value())));
```

The Diver resolves the supplied contract in the target process and discovers
its readable scalar properties, including inherited interfaces. It caches
metadata only. Primitive values, nullable scalars, strings, supported value
classes, and enums are included. Complex members require explicit projections;
properties outside the contract are not traversed. Getter failures identify the
output member path and reject that row.

There is no SDK table mapping `Power`, `IsTriggeredAbility`,
`HasSummoningSickness`, `CurrentLevel`, or future scalar members to
`MagicProperty`. Those values come from the resolved native card. The adapter
also consumes native counter and ability dictionaries; it no longer normalizes
property names to reconstruct those collections. Numeric values unknown to the
SDK enums survive local adaptation.

Definition scalar members come from the native `ICardDefinition` contract,
including its inherited `IMagicEntityDefinition.CTN`. `CollectionItem.CTN`
exposes that getter; `GameCard.CTN` reads it through `Definition`. When no
resolved definition exists, the SDK retains the reported texture ID. Native
revealed-zone cards retain their captured definitions.

Blue text comes from the native `BlueTextString` ability's `Name`, already
assembled by MTGO from its numbered text slots. The capture retains the native
ability dictionary and descriptions; the SDK only normalizes display markup.
Type text continues to use native `ConstructCardTypeText`.

The SDK exposes `IsTransformed` through the native `IS_TRANSFORMED` property
accessor. `IsFlipped` continues to read the native flip-card getter. These are
separate states. Token and land flags also use the native property accessor.
Split-child IDs and the SDK's attachment display-reference priority remain
explicit property reads, with one implementation in `GameCard`.

## Local adapter and comparison

`GameCardCapture.Decode` preserves named outputs (`members`, `definition`,
`associations`, `typeLine`, and `previousZone`). It does not
merge raw properties into another card-member dictionary. `GameCardPartial`
retains those outputs, raw processor properties, and a `GameReferenceResolver`
in `Processors.Snapshots` that shares player and zone references across captures.
Native scalar reads forward to the captured member map.

Unchanged native card values are reused across messages, but each processing tick
receives its own local enrichment: top-level properties, combat lists, and target
information. Updating those values cannot change the adapter held by a previous
snapshot. Hidden cards follow the same rule. `GameContext.Previous` retains the
previous completed snapshot, including its timestamps and prompt identity. Scope
cleanup releases its dictionary references without clearing published dictionaries.


The adapter converts native counter/ability dictionaries once through checked,
generic local reads. Missing members and wrong types report the member path.
The adapter freezes scalar property values separately from the mutable
processor `Properties` container. Its generic `GetIntegerProperty`,
`GetBooleanProperty`, and `GetStringProperty` methods match native accessor
semantics: invariant integer parsing, flags true for values at least one,
integer-to-string conversion, explicit defaults, and successful null strings.
`GameCard` uses those same accessors for live objects and local adapters;
there are no separate live and snapshot extension recipes. Backfilling
processor properties does not change previously captured values.
Explicitly unsupported action lists and other-face navigation remain unavailable.

`s_memberReaders`, scalar comparison tables, `GetComparisonValues`, counter
name reconstruction, and ability name reconstruction are removed. Equality and
hashing use `RemoteBatchObject` structural comparison for native outputs:

- Objects and dictionaries compare by membership, independently of insertion order.
- Sequences compare by order.
- Enums retain and compare native identity, numeric value, and display name.
- Null remains distinct from an absent member.

A future captured member participates without adding a comparison field.
Comparison accesses captured values only, without reflecting live objects.

Target text, target IDs, and combat links are typed processor-owned fields on
the adapter. Enrichment does not rewrite native output dictionaries.
Comparison includes frozen scalar property values and these local fields. Combat lists
compare ordered `(Order, TargetId)` pairs directly; there is no serialized
second copy or commit step. Raw properties remain available for processor logic
and capture hashing; editing them does not reinterpret resolved scalar members.

Owner/controller/protector IDs come from native references. Live player and zone
wrappers continue to use the shared game reference scope. Definition data stays
local; explicit `Set.Cards` and `Set.ContainsCatalogId` navigation still resolves
the live collection by set code.

## Snapshot factory changes

Identified offline cards require recorded named capture outputs:

```csharp
var card = references.CreateCard(properties, capturedOutputs);
var placeholder = references.CreatePlaceholder(properties);

// Offline callers may also explicitly wrap their local adapter.
var offlineCard = new GameCard(new GameCardPartial(properties, game, capturedOutputs));
```

The former `FromProperties` reconstruction factory and unused multi-request
`GameCard.Create` wrapper are removed. Gameplay capture reads existing resolved
cards rather than constructing them.
Standalone property capture creates a native property-container copy. There is
no compatibility reconstruction path.

Construction belongs to the processor layer. `GameCard` exposes neither snapshot
nor placeholder factories; its ordinary constructor wraps the supplied object.
Unidentified synthetic cards use `references.CreatePlaceholder(properties)`.
They carry known display identity and reference metadata, with unavailable
definition and empty counters/abilities.
They do not manufacture defaults by reflecting over the SDK scalar surface.
`GameCardPartial.HasResolvedState` is false for placeholders. This describes
processor capture availability and is not a property of the `GameCard` wrapper.
Reads of unavailable statistics throw; Tracker's writer explicitly checks the
unwrapped processor adapter and records identity without querying those
statistics. Its existing nonnullable database flags use false defaults for these
identity records.

## Copying and method effects

Standalone property capture uses native `CopyFrom`, which locks the source root
while copying its dictionaries but shares nested container references.
Serialization detaches the returned tree; it does not establish an atomic
snapshot of concurrent native updates.

Gameplay capture reads completed native cards inside the gameplay postfix. It
performs no native card construction, property resolution, or zone mutation.
The generic batch API can execute methods with effects, and provides no rollback;
that capability is not needed to reconstruct cards in this capture path.

## Prompt options

`GamePrompt` wraps native prompt state and grouped option values. It holds no
owning game, processor, UI callback, or client revision. A plain `game.Prompt`
rereads native actions on each `Options` access; captured prompts contain the
actions and detached `PromptControl` values from their capture time.

```csharp
PromptOptionsSnapshot captured = PromptProcessor.CaptureOptions(game, prompt);
GamePrompt capturedPrompt = captured.Prompt;
var options = capturedPrompt.Options;
```

An initialized `PromptProcessor` also offers `CaptureOptions(prompt)` using its
game scope. Both forms retain the supplied actions and add unbacked UI buttons
only when the notification-driven capture's native prompt nonce matches.
The control revision is derived from the prompt nonce, interaction mode, and
button identities and states. Reading the cached controls performs no UI capture
or live prompt read. The static form does not register a snapshot processor.
Correlated prompt events carry an enriched prompt produced by the same capture path.

The dictionary and its lists are read-only. Reading captured `Options` does not
query the client or update old controls; callers explicitly capture again for
UI refreshes and execution checks. A plain live prompt rereads native actions;
a previously captured prompt retains its detached action values. Recapturing an
older prompt does not pair it with newer controls. This enrichment is not an
atomic snapshot of gameplay and WPF state. Native execution objects must be resolved and validated at command execution time;
captured action descriptions do not act as live handles.

## Processing ownership

`GameProcessor.ForGame(game)` is the sole construction path and owns the
reference scope, native action registry, interaction tracker, snapshot pipeline,
and processor-event cleanup for that `Game` wrapper. Obtaining it creates no
remote hooks. `InteractionState` attaches native notifications lazily. Accessing
`CardActions` starts correlated snapshot processing if the scope was inactive;
an already active scope retains its explicit `Ready()` boundary.

```csharp
var processing = GameProcessor.ForGame(game);
game.OnStateChanged += update =>
{
  // Enqueue the captured update for export; keep synchronous subscribers short.
  QueueForExport(update);
};
processing.Ready();
```

`Game.OnStateChanged` reports both completed gameplay processing and local
interaction changes through the processor event bus. Its `Nonce` identifies the
combined update, while `Snapshot.Nonce` and `Prompt.Nonce` retain the native server
correlation identity. `Interaction` carries detached mode, displayed text,
controls, card actions and card/player selection flags; `Options` contains the corresponding prompt controls
and their execution revision. These values do not refresh when read.

The tracker detaches native prompt identity alongside UI values inside the native
notification, before delivery is queued. A later SDK roundtrip does not select
which prompt that notification belongs to.
A capture arriving ahead of gameplay processing is retained by stream identity
and state revision, rather than prompt nonce alone: several gameplay messages can
retain the same prompt nonce. The completed gameplay event uses its explicitly
paired UI capture. Later completed local transitions for that revision are then
published in native order against that message's snapshot. A boundary without
relevant UI notifications retains the preceding captured values. Before UI
readiness, native prompt actions remain publishable with empty interaction state.
A missing required capture after readiness is an explicit correlation failure,
not a successful empty capture. Repeated equivalent interaction values
for the same snapshot produce no additional unified event. No debounce timer or
wait for an empty processing queue is involved. Existing `OnPromptChanged`
subscribers continue to receive the server nonce.

`update.Interaction.Cards` maps Diver logical addresses to captured `CardId`,
`SourceId`, `Name`, `Actions`, and `Selection` (`HasActions`, `IsSelectable`,
`IsTargetable`, `IsTargeted`).
Action properties and target identities are detached inside the native notification.
Action card/player references are then resolved against that update's server
snapshot. Empty action lists and removed card views replace previous values.
Piles and inert visual copies use the primary `CardRelationshipData.CardViewModel`;
they do not introduce additional choices for the same card identity.

```csharp
game.OnStateChanged += update =>
{
  foreach (var (address, card) in update.Interaction.Cards)
    RecordCandidates(update.Nonce, address, card.CardId, card.SourceId,
      card.Actions, card.Selection);
};
```

The tracker also observes completed native `UpdateViews` and
`UpdateZoneViews` calls. These cover card-only eligibility/action changes and
native target-list edits while mode and prompt text stay fixed. Such changes
advance the unified event nonce. Repeated equivalent captures do not.
New card hooks send detached values without live callback parameter handles.
Captured batches execute on the originating native thread with no nested IPC;
any failed card row fails the complete interaction capture explicitly.

Diver callback sending and SDK callback delivery each use one FIFO queue.
A slow earlier callback therefore cannot be overtaken by a later callback.
Request/response handling remains independent, allowing reflection requests
from callback handlers. Keep synchronous subscribers short to avoid holding
subsequent callback delivery.

For historical processing, use the event's `Interaction.Cards`. Registry getters
expose the latest capture, which may have advanced since a retained event.
Read `CardActions.Snapshot` once when reading actions and eligibility together
outside the game event pipeline; separate getter calls can span revisions.

`InteractionState.Changed` reports changes to mode, displayed prompt text,
prompt controls, native card actions, and card/player selection flags. Its mode argument can remain
unchanged when another captured value changes.
`InteractionState.Current` returns captured values without performing IPC.
The prompt box's `Text` property retains one `PropertyChangeRouter` subscription.
Card eligibility and actions, player flags, and button enabled/visible flags use
the shared native property hook described above. Collection notifications observe
membership changes without installing subscriptions on individual items.
Each notification contributes to its enclosing native scope, or supplies a
standalone completion when there is no enclosing operation. A capture failure is
delivered explicitly and never replaced by a later live read.
The tracker retries attachment during processor access while card views are
unavailable. It subscribes to native `DuelViewLoaded` before reading the retained
`DuelView` reference, covering attachment before or after that notification.
Once a view exists, `MTGOSDK.API.Interface.Dispatcher.WaitForReadyAsync(view)`
subscribes to its WPF `Loaded` event and queues its native `IsLoaded` getter at
`ContextIdle` priority. It awaits operation completion and checks retained
operation status, covering completion before the callback subscription. An
unloaded result waits for a load notification before another check; no repeated
IPC polling or five-second synchronous dispatch wait is used for readiness.

Fixed zone collection subscription setup and the initial capture request begin
only after this readiness check succeeds. Mode, text, and collection
notifications cannot publish interaction state before that gate succeeds.
Initialization waits separately from server snapshot processing and is cancelled
on disposal or view replacement.
Readiness establishes that the loaded view's dispatcher serviced the check;
it does not guarantee that subsequent UI work cannot block. Later notification
captures run synchronously on their native calling thread without redispatch.
The tracker keeps valid subscriptions if capture fails and replaces them when
the mode hook observes a recreated scene.

`GameCard.ActionsChanged` reports a full captured `GameCard` with the action list
from its paired UI capture:

```csharp
GameCard.ActionsChanged += (card, actions) =>
{
  RecordActions(card.Name, card.TypeLine, actions);
};
```

The event uses the existing gameplay hook and correlated publication rather than
an independent `Actions_CollectionChanged` hook. Subscribing attaches the registry
to active processing scopes. Games without an active scope begin observation on
their next gameplay message. The subscription does not replay existing action lists.
Unsubscribing removes the global handler without disposing shared game processing.

`CardActions.Changed` offers the same card and action list for a specific registry.
The owner and action references resolve by logical address against visible, hidden,
and revealed cards in the corresponding gameplay snapshot. Removal reports the
last resolved owner with an empty action list. A missing owner is logged explicitly;
identity-only values are never presented as a complete `GameCard`.

`CardActions.GetActions()`, `GetSelectable()`, `GetPromptButtons()`, and
`GetPlayerSelectable()` return the latest correlated lists without IPC after the
first completed publication. Player entries use the native `GamePlayer.Id`
(server player index), independent of scene display order. Changed lists are
replaced, so previously returned lists retain their values. Commands still resolve
and validate live controls at execution time.
Notifications are delivered asynchronously; these are the latest observed
values, not a guarantee of lossless history or atomicity with a server snapshot.
Before initial capture, mode is `Undefined` and the captured lists are empty.
Publication replaces one immutable state reference, so readers do not wait for
an in-progress UI capture.

Existing native `Game` members and gameplay event signatures remain on `Game`.
The added `References`, `CardActions`, `InteractionState`, registration, readiness,
and waiting APIs belong to the processor layer. Custom processors use
`processing.Register(processor)`.

`game.ClearEvents()` clears native event handlers and delegates processing
cleanup for the game, including a scope activated through the global card event.
Remote client disposal also releases processing scopes. Cleanup removes routing
and gameplay handlers immediately, then
releases owned components after an in-flight snapshot finishes. Subsequent
access creates a fresh scope; old scopes reject further registration and
resource access. Event wrappers can be subscribed again against that new scope.
Native reference sharing is retired before publishing the replacement scope,
so deferred cleanup cannot invalidate its references.

## Revealed cards

The gameplay completion capture reads each player's native `Revealed` zone as
well as the message's `ThingElements`. Separate `GAME_PLAYER_REVEALS_CARD`
messages create client-side zone objects whose server IDs can all be `-1`. Those
objects do not need a corresponding message row to appear in
`GameStateSnapshot.RevealedCards`, which is keyed by native logical address.

`OnRevealedCards` combines this collection with temporarily exposed library and
pile cards from `HiddenCards`. Arrivals and departures compare native identities,
and departed cards retain their last captured values. Two copies with the same
name or server ID remain distinct. Repeated snapshots do not imply expiry.

This collection describes native zone membership. Dismissing a card in MTGO's
reveal view hides its view model without removing the native zone object.
