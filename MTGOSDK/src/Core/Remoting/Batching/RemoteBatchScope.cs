/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// Records ordered reflection operations in the root batch or a collection body.
/// </summary>
public class RemoteBatchScope
{
  internal RemoteBatch Owner = null!;
  internal RemoteBatchScope? Parent;
  internal readonly List<RemoteBatchOperation> Operations = [];
  private readonly int? _itemScope;

  internal RemoteBatchScope(RemoteBatch owner, RemoteBatchScope parent, int itemScope)
  {
    Owner = owner;
    Parent = parent;
    _itemScope = itemScope;
  }

  internal RemoteBatchScope()
  {
  }

  /// <summary>
  /// Refers to the current collection item. Available only in a ForEach body.
  /// </summary>
  public RemoteBatchSlot Item => _itemScope.HasValue
    ? new(Owner, this, new() { Kind = RemoteBatchReferenceKind.Item, Id = _itemScope.Value })
    : throw new InvalidOperationException("Item is available only in a ForEach body.");

  protected RemoteBatchReference Reference(RemoteBatchSlot slot)
  {
    if (slot.Owner != Owner)
      throw new ArgumentException("Reference belongs to another batch.");
    for (var scope = this; scope != null; scope = scope.Parent)
      if (scope == slot.Scope)
        return slot.Reference;
    throw new ArgumentException("Reference belongs to a child or sibling scope.");
  }

  private RemoteBatchSlot Add(RemoteBatchOperation operation)
  {
    operation.Id = Owner.NextId++;
    Operations.Add(operation);
    return new(Owner, this, new() { Kind = RemoteBatchReferenceKind.Result, Id = operation.Id });
  }

  /// <summary>
  /// Records a dot-separated property or field read from an input or earlier result.
  /// </summary>
  public RemoteBatchSlot Read(RemoteBatchSlot target, string path) =>
    Add(new() { Kind = RemoteBatchOperationKind.Read, Target = Reference(target), Path = path });

  /// <summary>
  /// Records a member read from the current collection item.
  /// </summary>
  public RemoteBatchSlot Read(string path) => Read(Item, path);

  /// <summary>
  /// Records construction using an exact constructor signature.
  /// </summary>
  /// <remarks>
  /// Constructed objects are not automatically disposed after execution. Record
  /// a disposal call when the object requires cleanup.
  /// </remarks>
  public RemoteBatchSlot Create(RemoteBatchMethod constructor, params RemoteBatchSlot[] arguments) =>
    Add(new()
    {
      Kind = RemoteBatchOperationKind.Create,
      Method = constructor,
      Arguments = arguments.Select(Reference).ToArray()
    });

  /// <summary>
  /// Records an instance call using an exact method signature. A void call runs
  /// for its effects and cannot supply a value to later operations.
  /// </summary>
  public RemoteBatchSlot Call(RemoteBatchSlot target, RemoteBatchMethod method,
    params RemoteBatchSlot[] arguments) => Add(new()
    {
      Kind = RemoteBatchOperationKind.Call,
      Target = Reference(target),
      Method = method,
      Arguments = arguments.Select(Reference).ToArray()
    });

  /// <summary>
  /// Records a static call using an exact method signature.
  /// </summary>
  public RemoteBatchSlot CallStatic(RemoteBatchMethod method, params RemoteBatchSlot[] arguments) =>
    Add(new()
    {
      Kind = RemoteBatchOperationKind.CallStatic,
      Method = method,
      Arguments = arguments.Select(Reference).ToArray()
    });

  /// <summary>
  /// Filters a collection by comparing a member path with a batch value.
  /// </summary>
  public RemoteBatchSlot Where(RemoteBatchSlot source, string path,
    ComparisonOperator comparison, RemoteBatchSlot value) => Where(source, new()
    {
      Kind = RemoteBatchPredicateKind.Comparison,
      Path = path,
      Operator = comparison,
      Value = Reference(value)
    });

  /// <summary>
  /// Filters a collection by comparing two member paths on each item.
  /// </summary>
  public RemoteBatchSlot WhereProperty(RemoteBatchSlot source, string leftPath,
    ComparisonOperator comparison, string rightPath) => Where(source, new()
    {
      Kind = RemoteBatchPredicateKind.Comparison,
      Path = leftPath,
      OtherPath = rightPath,
      Operator = comparison
    });

  /// <summary>
  /// Keeps nonnull collection items assignable to the supplied native type.
  /// </summary>
  public RemoteBatchSlot WhereAssignableTo(RemoteBatchSlot source, RemoteBatchType type) =>
    Where(source, new() { Kind = RemoteBatchPredicateKind.AssignableTo, Type = type });

  /// <summary>
  /// Filters a collection by an enum member's name, ignoring case.
  /// </summary>
  public RemoteBatchSlot WhereEnumName(RemoteBatchSlot source, string path, string name) =>
    Where(source, new()
    {
      Kind = RemoteBatchPredicateKind.EnumName,
      Path = path,
      Value = Reference(Owner.Input(name))
    });

  /// <summary>
  /// Filters a collection by a substring in a string member path.
  /// </summary>
  public RemoteBatchSlot WhereContains(RemoteBatchSlot source, string path, string text,
    bool ignoreCase = true) => Where(source, new()
    {
      Kind = RemoteBatchPredicateKind.StringContains,
      Path = path,
      Value = Reference(Owner.Input(text)),
      IgnoreCase = ignoreCase
    });

  private RemoteBatchSlot Where(RemoteBatchSlot source, RemoteBatchPredicate predicate) =>
    Add(new()
    {
      Kind = RemoteBatchOperationKind.Where,
      Target = Reference(source),
      Predicate = predicate
    });

  /// <summary>
  /// Orders collection items by a member path, preserving source order for equal keys.
  /// </summary>
  public RemoteBatchSlot OrderBy(RemoteBatchSlot source, string path, bool descending = false) =>
    Add(new()
    {
      Kind = RemoteBatchOperationKind.OrderBy,
      Target = Reference(source),
      Path = path,
      Descending = descending
    });

  /// <summary>
  /// Counts a collection without serializing or pinning its items.
  /// </summary>
  public RemoteBatchSlot Count(RemoteBatchSlot source) =>
    Add(new() { Kind = RemoteBatchOperationKind.Count, Target = Reference(source) });

  /// <summary>
  /// Records a named output, serialized by value in the target process.
  /// </summary>
  public void Output(string name, RemoteBatchSlot value, RemoteBatchProjection? projection = null) =>
    Add(new()
    {
      Kind = RemoteBatchOperationKind.Output,
      Target = Reference(value),
      OutputName = name,
      Projection = projection ?? RemoteBatchProjection.Value()
    });

  /// <summary>
  /// Records a body to execute for every source item.
  /// </summary>
  public void ForEach(RemoteBatchSlot source, Action<RemoteBatchScope> body) =>
    ForEach(source, null, body);

  /// <summary>
  /// Records the collection body once and executes selected rows in source order.
  /// Pass null indices to select every row or an empty array to select none.
  /// </summary>
  public void ForEach(RemoteBatchSlot source, int[]? selectedIndices, Action<RemoteBatchScope> body)
  {
    var operation = new RemoteBatchOperation
    {
      Kind = RemoteBatchOperationKind.ForEach,
      Target = Reference(source),
      Indices = selectedIndices?.ToArray()
    };
    Add(operation);
    var scope = new RemoteBatchScope(Owner, this, operation.Id);
    try
    {
      body(scope);
      operation.Body = scope.Operations;
    }
    catch
    {
      Operations.Remove(operation);
      throw;
    }
  }
}
