/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// Validates request structure before any target operations execute.
/// </summary>
internal static class BatchRequestValidator
{
  public static void Validate(RemoteBatchRequest request)
  {
    if (request == null || request.MaxDepth < 1 || request.Inputs == null ||
        request.Operations == null || request.Projections == null)
      throw new ArgumentException("Invalid batch request or depth limit.");
    if (request.Inputs.Any(input => input == null))
      throw new ArgumentException("Batch inputs cannot be null descriptors.");

    foreach (var projection in request.Projections)
    {
      RequireName(projection.Key);
      ValidateProjection(projection.Value, request.Projections, new());
    }

    var results = new HashSet<int>();
    ValidateSequence(request, request.Operations, results, new(), new());
    if (request.ReferenceOutput != null)
      CheckReference(request, request.ReferenceOutput, results, new());
  }

  private static void ValidateSequence(RemoteBatchRequest request,
    List<RemoteBatchOperation> operations, HashSet<int> results,
    HashSet<int> items, HashSet<int> allIds)
  {
    var outputs = new HashSet<string>(StringComparer.Ordinal);
    foreach (var operation in operations)
    {
      if (operation == null || operation.Id < 0 || !allIds.Add(operation.Id) ||
          operation.Arguments == null || operation.Body == null)
        throw new ArgumentException("Invalid or duplicate operation ID.");
      if (operation.Kind != RemoteBatchOperationKind.ForEach &&
          (operation.Body.Count != 0 || operation.Indices != null))
        throw new ArgumentException("Only ForEach accepts a body or selected indices.");
      if ((operation.Kind == RemoteBatchOperationKind.Read || operation.Kind == RemoteBatchOperationKind.Output ||
          operation.Kind == RemoteBatchOperationKind.ForEach || operation.Kind == RemoteBatchOperationKind.Where ||
          operation.Kind == RemoteBatchOperationKind.OrderBy || operation.Kind == RemoteBatchOperationKind.Count) &&
          operation.Arguments.Length != 0)
        throw new ArgumentException("This operation does not accept arguments.");

      // Nested bodies inherit the results available at their entry point.
      // Sharing allIds across bodies keeps operation IDs unique throughout the batch.
      void CheckReference(RemoteBatchReference? reference) =>
        BatchRequestValidator.CheckReference(request, reference, results, items);

      foreach (var argument in operation.Arguments)
        CheckReference(argument);

      switch (operation.Kind)
      {
        case RemoteBatchOperationKind.Read:
          CheckReference(operation.Target);
          RequirePath(operation.Path);
          break;

        case RemoteBatchOperationKind.Where:
          CheckReference(operation.Target);
          ValidatePredicate(operation.Predicate, CheckReference);
          break;

        case RemoteBatchOperationKind.OrderBy:
          CheckReference(operation.Target);
          RequirePath(operation.Path);
          break;

        case RemoteBatchOperationKind.Count:
          CheckReference(operation.Target);
          break;

        case RemoteBatchOperationKind.Take:
          CheckReference(operation.Target);
          if (operation.Arguments.Length != 1)
            throw new ArgumentException("Take requires one count argument.");
          break;

        case RemoteBatchOperationKind.Create:
        case RemoteBatchOperationKind.Call:
        case RemoteBatchOperationKind.CallStatic:
          if (operation.Kind == RemoteBatchOperationKind.Call)
            CheckReference(operation.Target);
          var method = operation.Method;
          if (method == null || method.ParameterTypes == null || method.GenericArguments == null ||
              method.ParameterTypes.Length != operation.Arguments.Length)
            throw new ArgumentException("Missing exact method signature or wrong argument count.");
          RequireType(method.DeclaringType);
          RequireName(method.Name);
          foreach (var type in method.ParameterTypes.Concat(method.GenericArguments))
            RequireType(type);
          if (operation.Kind == RemoteBatchOperationKind.Create &&
              (method.Name != ".ctor" || method.GenericArguments.Length != 0))
            throw new ArgumentException("Create requires a nongeneric .ctor signature.");
          if (operation.Kind != RemoteBatchOperationKind.Create && method.Name == ".ctor")
            throw new ArgumentException("Constructors require Create.");
          break;

        case RemoteBatchOperationKind.ForEach:
          CheckReference(operation.Target);
          if (operation.Indices != null && (operation.Indices.Any(i => i < 0) ||
              operation.Indices.Distinct().Count() != operation.Indices.Length))
            throw new ArgumentException("Selected indices must be unique and nonnegative.");
          var childItems = new HashSet<int>(items) { operation.Id };
          ValidateSequence(request, operation.Body, new HashSet<int>(results), childItems, allIds);
          continue;

        case RemoteBatchOperationKind.Output:
          CheckReference(operation.Target);
          RequireName(operation.OutputName);
          if (!outputs.Add(operation.OutputName!))
            throw new ArgumentException("Duplicate output name.");
          ValidateProjection(operation.Projection, request.Projections, new());
          continue;

        default:
          throw new ArgumentException("Unknown batch operation.");
      }
      results.Add(operation.Id);
    }
  }

  private static void ValidatePredicate(RemoteBatchPredicate? predicate,
    Action<RemoteBatchReference?> checkReference)
  {
    if (predicate == null)
      throw new ArgumentException("Missing collection predicate.");

    switch (predicate.Kind)
    {
      case RemoteBatchPredicateKind.AssignableTo:
        RequireType(predicate.Type!);
        break;

      case RemoteBatchPredicateKind.Comparison:
        RequirePath(predicate.Path);
        if (!Enum.IsDefined(typeof(ComparisonOperator), predicate.Operator))
          throw new ArgumentException("Unknown comparison operator.");
        if (predicate.OtherPath != null)
          RequirePath(predicate.OtherPath);
        else
          checkReference(predicate.Value);
        break;

      case RemoteBatchPredicateKind.EnumName:
      case RemoteBatchPredicateKind.StringContains:
        RequirePath(predicate.Path);
        checkReference(predicate.Value);
        break;

      default:
        throw new ArgumentException("Unknown collection predicate.");
    }
  }

  private static void CheckReference(RemoteBatchRequest request, RemoteBatchReference? reference,
    HashSet<int> results, HashSet<int> items)
  {
    if (reference == null)
      throw new ArgumentException("Missing operation reference.");
    bool valid = reference.Kind switch
    {
      RemoteBatchReferenceKind.Input => reference.Id >= 0 && reference.Id < request.Inputs.Count,
      RemoteBatchReferenceKind.Result => results.Contains(reference.Id),
      RemoteBatchReferenceKind.Item => items.Contains(reference.Id),
      _ => false
    };
    if (!valid)
      throw new ArgumentException($"Invalid, forward, or inaccessible reference {reference.Id}.");
  }

  private static void ValidateProjection(RemoteBatchProjection projection,
    Dictionary<string, RemoteBatchProjection> definitions, HashSet<RemoteBatchProjection> visited)
  {
    if (projection == null)
      throw new ArgumentException("Missing projection.");
    if (!visited.Add(projection))
      return;
    foreach (var alternative in projection.RuntimeTypes)
    {
      RequireName(alternative.Key);
      ValidateProjection(alternative.Value, definitions, visited);
    }
    if (projection.IncludeLogicalAddress && projection.Kind != RemoteBatchProjectionKind.Object)
      throw new ArgumentException("Logical addresses require an object projection.");
    if (projection.RuntimeScalars && projection.Kind != RemoteBatchProjectionKind.Object)
      throw new ArgumentException("Runtime scalar members require an object projection.");
    if (projection.ScalarContract != null)
    {
      if (projection.Kind != RemoteBatchProjectionKind.Object)
        throw new ArgumentException("Scalar contracts require an object projection.");
      RequireType(projection.ScalarContract);
    }
    switch (projection.Kind)
    {
      case RemoteBatchProjectionKind.Value:
        break;

      case RemoteBatchProjectionKind.Named:
        RequireName(projection.Name);
        // Named aliases must eventually resolve to a concrete projection.
        // Recursive schemas remain valid when they refer back through a member
        // or collection element instead of forming a cycle of aliases.
        var names = new HashSet<string>(StringComparer.Ordinal);
        var resolved = projection;
        while (resolved.Kind == RemoteBatchProjectionKind.Named)
        {
          if (resolved.Name == null || !names.Add(resolved.Name) ||
              !definitions.TryGetValue(resolved.Name, out resolved))
            throw new ArgumentException("Unknown projection or cyclic projection aliases.");
        }
        ValidateProjection(resolved, definitions, visited);
        break;

      case RemoteBatchProjectionKind.Sequence:
        ValidateProjection(projection.Element!, definitions, visited);
        break;

      case RemoteBatchProjectionKind.Dictionary:
        ValidateProjection(projection.DictionaryKey!, definitions, visited);
        ValidateProjection(projection.DictionaryValue!, definitions, visited);
        break;

      case RemoteBatchProjectionKind.Object:
        if (projection.Members == null)
          throw new ArgumentException("Missing object members.");
        foreach (var member in projection.Members)
        {
          RequireName(member.Key);
          if (member.Value == null)
            throw new ArgumentException("Missing member projection.");
          RequirePath(member.Value.Path);
          ValidateProjection(member.Value.Projection, definitions, visited);
        }
        break;
      default:
        throw new ArgumentException("Unknown projection kind.");
    }
  }

  private static void RequireType(RemoteBatchType type)
  {
    if (type == null)
      throw new ArgumentException("Missing type identity.");
    RequireName(type.FullName);
    if (type.GenericArguments == null)
      throw new ArgumentException("Missing generic type arguments.");
    foreach (var argument in type.GenericArguments)
      RequireType(argument);
    if (type.ElementType != null)
    {
      RequireType(type.ElementType);
      if ((type.IsByRef && (type.ArrayRank != 0 || type.IsVector)) ||
          (!type.IsByRef && (type.ArrayRank < 1 || type.ArrayRank > 32 || (type.IsVector && type.ArrayRank != 1))) ||
          type.GenericArguments.Length != 0)
        throw new ArgumentException("Invalid element type shape.");
    }
    else if (type.IsByRef || type.ArrayRank != 0 || type.IsVector)
      throw new ArgumentException("Missing element type identity.");
  }

  private static void RequireName(string? name)
  {
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Names cannot be empty.");
  }

  private static void RequirePath(string? path)
  {
    RequireName(path);
    if (path!.Split('.').Any(string.IsNullOrWhiteSpace))
      throw new ArgumentException("Invalid member path.");
  }
}
