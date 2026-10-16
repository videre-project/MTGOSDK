# Batched reflection

`MTGOSDK.Core.Remoting.Batching.RemoteBatch` records a sequence of reflection
operations locally. `Execute` or `ExecuteAsync` sends one `/batch_execute`
request. The Diver runs the sequence and returns explicitly projected values.
Existing `/invoke`, `/batch_members`, and `/batch_collection` callers continue
using their existing protocols.

## API layout

`MTGOSDK.Core.Remoting.Batching` contains the local builder, its scope/slot
references, the executor and serializer, and materialized local values.
`MTGOSDK.Core.Remoting.Interop.Interactions.Batching` contains the MessagePack
request, operation, projection, and result types. Each top-level type has its own
file. Hook and event capture descriptors live with the callback protocol under
`Interop.Interactions.Callbacks`.

Callers that describe signatures or projections import both batching namespaces;
the wire types' MessagePack keys and enum values are unchanged by this organization.

## Recording a request

The following example uses ordinary fixture types. Assume the target has a
`Fixtures.Message` with an `Elements` collection, each element has `Properties`
and `PreviousZone`, and `Fixtures.Container` has a parameterless constructor,
`CopyFrom(Fixtures.Properties)`, and static `Describe(Fixtures.Container)`.
`messageHandle` is an **already acquired** remote handle. These type names and
signatures are supplied by the caller; recording does not discover remote types.

```csharp
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

var containerType = new RemoteBatchType("Fixtures.Container", "Fixtures");
var propertiesType = new RemoteBatchType("Fixtures.Properties", "Fixtures");
var constructor = new RemoteBatchMethod(containerType, ".ctor");
var copyFrom = new RemoteBatchMethod(containerType, "CopyFrom", propertiesType);
var describe = new RemoteBatchMethod(containerType, "Describe", containerType);

var batch = new RemoteBatch { ForceUIThread = false, MaxDepth = 32 };
var source = batch.Input(messageHandle);
var elements = batch.Read(source, "Elements");
batch.ForEach(elements, new[] { 3, 1 }, row =>
{
    var properties = row.Read("Properties");
    var copy = row.Create(constructor);
    row.Call(copy, copyFrom, properties);
    row.Output("previousZone", row.Read("PreviousZone"));
    row.Output("description", row.CallStatic(describe, copy));
});

RemoteBatchResult result = await batch.ExecuteAsync(connection);
foreach (var row in result.Rows)
{
    if (row.Error is { } error)
        Console.WriteLine($"Source {row.SourceIndex}: operation {error.OperationId}: {error.Message}");
    else
        Console.WriteLine($"Source {row.SourceIndex}: {row.Values["description"].GetScalar<string>()}");
}
```

The recording lambda runs locally once; no delegate or SDK executable code is
transferred. The example executes source indices **1, then 3**, regardless of
the order in which the caller listed them. Omit indices to select all rows;
an empty index array selects none.

Inputs accept `ObjectOrRemoteAddress`, an existing raw `RemoteObject` or
`DynamicRemoteObject`, or literals supported by the existing parameter encoder.
SDK wrappers and interface proxies must first be converted to an existing raw
handle outside the builder. Raw handle recording reads the token without fetching
remote type metadata. Inputs remain valid only while their existing handles do;
the builder does not extend their remote lifetime.

The builder copies encoded inputs and clears their creation-time metadata.
Independently recorded capture descriptions can therefore share a native hook.
This leaves the supplied inputs unchanged and preserves literal values, including
DateTime values encoded in the payload.

`Read` supports dot-separated property/field paths, including nonpublic and
inherited members. A null along a path produces null. Missing members produce
an error. Indexer syntax is unsupported.
`EnumValue` reads an enum's underlying numeric value, matching the access path
exposed by SDK enum proxies and the existing batch member endpoints.

Method descriptors require the declaring type, exact parameter types, and any
generic method arguments. Null arguments do not trigger overload inference.
`RemoteBatchType.From(typeof(int))` provides a portable core-library identity,
and `From(typeof(List<int[]>))` records the generic definition and nested element
identities separately. This avoids embedding the caller's CLR-specific core
assembly into a constructed signature. Explicit assembly names resolve against
loaded target assemblies and never fall back to a different assembly. Omitted
assembly names use the existing runtime resolver. Use native identities for
types that exist only in the target.

References may address earlier results in the current or an enclosing scope.
They cannot reference later operations, other batches, sibling rows, or child
scopes. Void calls run for their effects, but their results cannot be used as
values. Constructor results are request-local and are not automatically disposed;
record an explicit `Dispose` call when necessary.

## Collection queries

Collection operations compose with reads and calls inside the same request:

```csharp
var batch = new RemoteBatch();
var items = batch.Read(batch.Input(existingRoot), "Items");
var available = batch.WhereProperty(items, "LockedQuantity",
    ComparisonOperator.LessThan, "Quantity");
var ordered = batch.OrderBy(available, "Name");
batch.Output("count", batch.Count(ordered));
batch.ForEach(ordered, row =>
{
    row.Output("id", row.Read("Id"));
    row.Output("name", row.Read("Name"));
});
var result = await batch.ExecuteAsync(connection);
```

`Where` compares a member path with an input or earlier result. `WhereProperty`
compares two paths on the same item. Both support the six `ComparisonOperator`
values and use the target runtime's default object comparer. Filters omit null
items; null member values participate in comparisons normally.

`WhereAssignableTo` resolves a caller-supplied native type identity and includes
instances implementing that type or interface. `WhereEnumName` compares enum
names without case sensitivity. `WhereContains` uses ordinal substring matching
and can optionally respect case. All member paths use the executor's existing
cached property/field reader, including nested paths and nonpublic members.

`OrderBy` reads each item's key once and preserves source order for equal keys.
Null keys sort first in ascending order and last in descending order. `Count`
uses ICollection.Count when available and otherwise enumerates without returning
item values. A collection source must be enumerable and cannot be a string.
ForEach indices refer to the collection supplied to it, including any preceding
filtering or sorting. Query intermediates remain local to the request.

### Live result ownership

A caller that needs native access to a query result can explicitly retain one
root value rather than record a detached output:

```csharp
var remoteCollection = await batch.ExecuteReferenceAsync(ordered, connection);
```

Here `connection` is a `RemoteHandle`. The returned `RemoteObject` follows the
same reference lifetime and release behavior as an ordinary remote invocation.
Only the requested root object is pinned; its contained items keep their native
identities without receiving separate pins. A null root returns null. Primitive
value types and child-row results cannot be retained through this terminal.

The wire request uses `ReferenceOutput` to select the root and returns its handle
in `RemoteBatchResult.Reference`. Ordinary Execute requests leave this field
unset and return detached values. Hook and event capture registrations reject
reference outputs so callback capture retains its existing ownership behavior.

## Projections and returned values

`Output(name, slot)` defaults to a value projection, accepting null, supported
scalars, enums, and ordered sequences of such values. Complex objects and
dictionaries require explicit projections. There is no implicit object-to-string
conversion or creation of returned remote handles.

```csharp
batch.DefineProjection("node", RemoteBatchProjection.Object(
    ("name", "Name", RemoteBatchProjection.Value()),
    ("children", "Children", RemoteBatchProjection.Dictionary(
        RemoteBatchProjection.Value(), RemoteBatchProjection.Named("node")))));
batch.Output("tree", root, RemoteBatchProjection.Named("node"));
```

`ObjectScalars(contract, explicitMembers...)` derives readable scalar properties
from a caller-supplied target-side contract, including inherited interfaces.
Discovery and getter calls happen in the target process during serialization.
Complex properties remain explicit; properties outside the contract are not
read. Explicit member projections override matching contract member names.
Contract bindings cache metadata, not input objects. A getter failure reports
the member's output path under the existing row failure policy.

The parameterless `ObjectScalars()` discovers readable public scalar properties
on the value's runtime type. `RuntimeTypes` maps a native full type name to an
alternative projection; it allows a heterogeneous sequence to add explicit
complex members for selected runtime types. This map and all member paths are
caller data. The Diver contains no domain-specific mapping. `RemoteBatchObject`
retains the native object type identity for local wrapper selection.

`WithLogicalAddress()` opts an object projection into capturing its native
logical address. The address is returned as `RemoteBatchObject.LogicalAddress`,
using the same token table as DRO handles. Assignment acquires no pin and does
not make the address remotely accessible; remote access still requires a retained
handle. The same native object keeps its address through unpinning and changes to
its own ID properties. Weak identity entries do not keep objects alive. Addresses
are scoped to the Diver runtime and are not persistent game or application IDs.
Local `WithMember` adaptations preserve this identity; structural comparisons
include it when present.

`RemoteBatchObject` materializes projected values locally and compares them
structurally: object/dictionary membership is independent of insertion order,
while sequence order is significant. `WithMember` returns a new member map for
local enrichment. Equality and hashing traverse captured values without remote
reflection. `Get<T>`, `GetSequence<T>`, and `GetDictionary<TKey, TValue>` provide
checked local reads, reporting missing members and invalid element types. Native
enums convert by numeric value, preserving values unknown to a caller's enum.
No other implicit scalar conversions or IPC occur during these reads.

Here `Children` is a dictionary whose values use the same named schema. Named
references make recursive schemas possible without cyclic DTO objects.

| Value kind | Returned representation |
| --- | --- |
| Null | `Kind = Null`; no value or error |
| Scalar | Native `Type`, MessagePack `ScalarData`; decode with `GetScalar<T>()` |
| Enum | Native `Type`, `UnderlyingType`, numeric `ScalarData`, optional `EnumName` |
| Sequence | Ordered `Items` |
| Object | Explicitly projected `Members`, keyed by output member names |
| Dictionary | `Entries`, each with separately tagged `Key` and `Value` |

Unknown enum numeric values survive unchanged; `EnumName` is null when the
value has no declared name. Dictionary keys retain their types rather than
being converted to strings. Scalar support includes CLR primitives except
`IntPtr`/`UIntPtr`, plus string, decimal, DateTime, DateTimeOffset, TimeSpan,
Guid, and byte arrays.

The default maximum output depth is 32, with an output root at depth zero.
Exceeding it is an error, including at a null leaf; no output is silently
truncated. Encountering the same object again along the active projection path
is a cycle error. Reusing a shared object in separate branches is allowed.

## Execution, failures, and dispatch

Operations and selected rows run sequentially. Each collection is fully
enumerated and its selected indices checked before its row operations begin.
Empty collections return no rows. Negative, duplicate, or absent indices are
invalid. Nested collections appear in their parent's `Rows`; each row carries
both its `CollectionOperationId` and `SourceIndex`.

A row failure stops that row, discards its partial named outputs and nested
rows, and continues subsequent source rows. `Error` reports the operation ID,
underlying exception, and output path when serialization failed. Successful
rows have `Error = null`. A successful null output remains `Kind = Null`.

Malformed descriptions, root-operation failures, and source-enumeration
failures use the existing request-level error envelope and cause the SDK call
to throw. Structural validation occurs before target execution. Signature
binding, checking an index against an actual collection, and output validation
occur during execution. Calls already executed before a failure retain their
effects: there is no rollback or atomic snapshot.

`ForceUIThread` applies to the entire request, including input resolution and
output serialization. The endpoint uses the existing STA dispatcher. An ambient
`DiverCommunicator.BeginUIThreadScope()` also enables this flag. For async calls,
dispose that existing scope on its creating thread before awaiting:

```csharp
Task<RemoteBatchResult> pending;
using (DiverCommunicator.BeginUIThreadScope())
    pending = batch.ExecuteAsync(connection);
var result = await pending;
```

Alternatively set `batch.ForceUIThread = true` directly. A detached request copy
prevents dispatch settings from mutating the original description. The builder
is mutable and is not safe for concurrent editing. Executor metadata caches are
shared between concurrent requests; inputs, constructed objects, and results
are local to each execution. Rows and intermediate results are never pinned.

V1 supports synchronous constructors/methods. Task/ValueTask-returning methods,
`ref`/`out`, open generic parameters, scripts, and conditional control flow are
unsupported. Native copying and synchronization are caller-described operations.
Native game interpretation, schemas, acceptance policies, and the existing
game-specific hash helper remain outside this extension.

## Synchronous capture in native hooks

`RemoteClient.HookMethod` and `RemoteHarmony.HookMethod` accept an optional
`RemoteHookCapture`. Its `Batch` is an ordinary recorded request; `Inputs` binds
leading request inputs to the native instance (`-1`) or a zero-based method
argument. Remaining inputs retain their encoded values. Bound inputs use
placeholders because binding requires no remote handles.

The Diver validates the description when subscribing, then executes its batch
at the configured hook position or at an enclosing capture scope's completion,
before enqueueing callback delivery. Input objects and
intermediates remain local to that invocation and are not pinned. Ordinary hook
sender/argument references still follow the existing callback protocol.
`ForceUIThread` is rejected for hook capture: capture runs on the thread executing
the native method and does not redispatch or issue IPC.

`HookContext.Capture` contains the projected results, including any row errors.
`CaptureError` records a request-level execution or serialization failure.
Capture errors are logged by the Diver and forwarded explicitly; the event
proxy reports them through existing callback error handling. A shared subscription
must have the same patch position and capture description. Captured values do not
provide rollback, synchronization with other threads, or live execution handles.

`RemoteHookCapture.Filters` optionally selects invocations by native instance
type and exact string argument values. Types are resolved once during registration;
derived instances match. The first matching filter can replace the instance batch
input with a member-path value. A null path value rejects that invocation.
Null and empty string arguments can be included explicitly in `ArgumentValues`.

Filtered hooks require a capture scope and an enrolled subscription for that exact
capture root. Removing the root's last subscription stops its captures, including
when a replacement root uses the same owner. Rejected invocations execute no batch,
pin no objects, and deliver no callback. The Diver interprets only the supplied
types, paths, and values; application-specific routing remains with the caller.

## Synchronous capture in native events

`RemoteEventCapture` attaches a batch description to an ordinary event
subscription. Outside an enrolled native scope, the Diver executes and projects it
inside the event handler. Inside a scope, it defers projection until that invocation
completes. Both paths detach values before queueing callback delivery.
Captured callbacks receive `HookContext.Capture`, `CaptureError`, the originating
timestamp, and `CaptureSequence` for enrolled captures. Sender
and event-argument handles are not implicitly returned or pinned for these callbacks.

`Inputs` maps batch input indices to native event parameter indices: `0` is the
sender and `1` is the event arguments. Unmapped inputs retain their encoded literals
or existing remote handles. Bindings are validated during subscription, and
`ForceUIThread` must be false: capture runs on the event's originating thread.
This does not establish thread affinity for events raised on another thread.

```csharp
var batch = new RemoteBatch();
var root = batch.Input(existingRoot);
batch.Output("state", batch.Read(root, "State"), stateProjection);
var capture = new RemoteEventCapture { Batch = batch.ToRequest() };
var changes = new EventProxy(existingSource, "Changed");
changes.Subscribe(context => Consume(context.Capture), capture);
```

`PropertyChangeRouter.Subscribe(source, batch, propertyNames)` uses the same
capture path and carries the detached payload in `CaptureContext`. Without an
enclosing scope, property names are detached in the notification handler; scoped
captures describe the grouped update at completion. Subscriptions with the same source and
capture description share their native event registration; different descriptions
remain independent. The native filter uses the union of properties requested by
subscriptions sharing the registration, and local delivery applies each subscriber's
filter. Inside an enrolled scope, matching notifications contribute to one capture
at completion. Filter changes retain the native event subscription. Capture work and native-thread cost should be
measured with the actual event source. Capture errors reach subscribers explicitly.

Capture removes the delivery-time reread of mutable state. It does not provide
rollback, stop unrelated concurrent writers, or imply globally ordered callback
delivery across independently scheduled event sources.

### Requesting an initial capture

A scoped property subscription can capture its retained inputs without raising a
native event:

```csharp
RemoteCaptureSequence completion = await subscription.CaptureAsync(forceUIThread: true);
```

The Diver executes the subscription's existing batch, assigns its position in the
native completion stream, and queues the detached result through the existing
callback. The response identifies that queued completion; it does not wait for SDK
callback delivery. Sender inputs use the subscribed source. Event-argument inputs
are null, and the property-name output is empty, because no native notification
occurred. The property router delivers the requested capture to its observers.
The callback's `RemoteCaptureSequence.Requested` flag and `SourceToken` distinguish
it from a native notification. Capture failures are reported through
`HookContext.CaptureError`.

`forceUIThread` dispatches this requested capture only. Ordinary notifications
continue to capture on their native calling thread. A queued completion remains
visible to shared-stream observers if its original subscriber detaches before
callback delivery, preserving the stream's ordering.
