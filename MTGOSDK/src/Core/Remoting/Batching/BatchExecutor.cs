/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// Executes ordered reflection operations and serializes their explicit outputs.
/// </summary>
/// <remarks>
/// <para>
/// Each execution keeps its inputs and intermediate results in local scopes.
/// Reflection bindings are cached so later executions can reuse the metadata.
/// </para>
/// <para>
/// The caller supplies type and input resolvers. Optional providers assign logical
/// identities and retain explicitly requested root results. The caller handles
/// transport and UI dispatch around execution.
/// </para>
/// </remarks>
public sealed class BatchExecutor(
  Func<RemoteBatchType, Type> resolveType,
  Func<ObjectOrRemoteAddress, object?> resolveInput,
  Func<object, ulong>? getObjectToken = null,
  Func<object, ObjectOrRemoteAddress>? retainObject = null)
{
  private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
  private static readonly object s_voidResult = new();
  private readonly ConcurrentDictionary<(Type Type, string Name), MemberInfo> _members = new();
  private readonly ConcurrentDictionary<(Type Type, string Signature), MethodBase> _methods = new();

  private sealed class Scope(Scope? parent = null)
  {
    public readonly Scope? Parent = parent;
    public readonly Dictionary<int, object?> Results = new();
    public readonly Dictionary<int, object?> Items = new();

    public object? Find(int id, bool item)
    {
      if ((item ? Items : Results).TryGetValue(id, out var value))
        return value;
      return Parent != null ? Parent.Find(id, item)
        : throw new InvalidOperationException($"Unresolved reference {id}.");
    }
  }

  /// <summary>
  /// Checks request structure and reference visibility without resolving types
  /// or invoking native members.
  /// </summary>
  public static void Validate(RemoteBatchRequest request) => BatchRequestValidator.Validate(request);

  /// <summary>
  /// Validates the request, resolves its encoded inputs, and executes its operations.
  /// </summary>
  /// <remarks>
  /// <para>
  /// A failed row returns its error and discards its outputs, then execution continues
  /// with the next source item. Invalid requests, root-operation failures, and failures
  /// enumerating any source collection abort the whole request.
  /// </para>
  /// <para>
  /// Native calls may have changed state before a later operation fails. Those effects
  /// remain after the failure because execution does not roll back completed calls.
  /// </para>
  /// </remarks>
  public RemoteBatchResult Execute(RemoteBatchRequest request)
  {
    BatchRequestValidator.Validate(request);
    return ExecuteValidated(request, request.Inputs.Select(resolveInput).ToArray());
  }

  /// <summary>
  /// Executes with local values replacing every encoded input, including native
  /// hook arguments. The array must contain one value per declared input.
  /// </summary>
  public RemoteBatchResult Execute(RemoteBatchRequest request, object?[] inputs)
  {
    BatchRequestValidator.Validate(request);
    if (inputs.Length != request.Inputs.Count)
      throw new ArgumentException("Input count does not match the batch description.", nameof(inputs));
    return ExecuteValidated(request, inputs);
  }

  /// <summary>
  /// Executes with local values replacing selected inputs by index. Other inputs
  /// are decoded by the supplied input resolver.
  /// </summary>
  public RemoteBatchResult Execute(RemoteBatchRequest request,
    IReadOnlyDictionary<int, object?> overrides)
  {
    BatchRequestValidator.Validate(request);
    if (overrides.Keys.Any(index => index < 0 || index >= request.Inputs.Count))
      throw new ArgumentException("Input override is outside the batch description.", nameof(overrides));
    var inputs = request.Inputs.Select((input, index) =>
      overrides.TryGetValue(index, out var value) ? value : resolveInput(input)).ToArray();
    return ExecuteValidated(request, inputs);
  }

  private RemoteBatchResult ExecuteValidated(RemoteBatchRequest request, object?[] inputs)
  {
    var serializer = new BatchValueSerializer(request, Read, Resolve, getObjectToken);
    var result = new RemoteBatchResult();
    var scope = new Scope();
    ExecuteSequence(request.Operations, scope, inputs, serializer, result.Values, result.Rows);
    if (request.ReferenceOutput != null)
    {
      var value = Value(request.ReferenceOutput, scope, inputs);
      if (value?.GetType().IsValueType == true)
        throw new ArgumentException("A retained output must be a reference object.");
      result.Reference = value == null ? ObjectOrRemoteAddress.Null
        : (retainObject ?? throw new InvalidOperationException("The executor cannot retain objects."))(value);
    }
    return result;
  }

  private object? Value(RemoteBatchReference reference, Scope scope, object?[] inputs)
  {
    var value = reference.Kind == RemoteBatchReferenceKind.Input ? inputs[reference.Id]
      : scope.Find(reference.Id, reference.Kind == RemoteBatchReferenceKind.Item);
    if (ReferenceEquals(value, s_voidResult))
      throw new InvalidOperationException("A void call cannot be used as a value.");
    return value;
  }

  private void ExecuteSequence(List<RemoteBatchOperation> operations, Scope scope,
    object?[] inputs, BatchValueSerializer serializer,
    Dictionary<string, RemoteBatchValue> outputs, List<RemoteBatchRow> rows)
  {
    foreach (var operation in operations)
    {
      try
      {
        object? Target() => Value(operation.Target!, scope, inputs);
        object?[] Arguments() => operation.Arguments.Select(a => Value(a, scope, inputs)).ToArray();
        switch (operation.Kind)
        {
          case RemoteBatchOperationKind.Read:
            scope.Results.Add(operation.Id, Read(Target(), operation.Path!));
            break;

          case RemoteBatchOperationKind.Create:
            var constructor = (ConstructorInfo)Bind(operation.Method!, null, operation.Kind);
            scope.Results.Add(operation.Id, constructor.Invoke(Arguments()));
            break;

          case RemoteBatchOperationKind.Call:
          case RemoteBatchOperationKind.CallStatic:
            var target = operation.Kind == RemoteBatchOperationKind.Call ? Target()
              ?? throw new NullReferenceException("Cannot invoke an instance method on null.") : null;
            var method = (MethodInfo)Bind(operation.Method!, target?.GetType(), operation.Kind);
            var value = method.Invoke(target, Arguments());
            scope.Results.Add(operation.Id, method.ReturnType == typeof(void) ? s_voidResult : value);
            break;

          case RemoteBatchOperationKind.Where:
            scope.Results.Add(operation.Id, Filter(operation.Id, Target(), operation.Predicate!,
              reference => Value(reference, scope, inputs)));
            break;

          case RemoteBatchOperationKind.OrderBy:
            var source = EnumerateCollection(Collection(Target()), operation.Id);
            var comparer = Comparer<object?>.Default;
            scope.Results.Add(operation.Id, operation.Descending
              ? source.OrderByDescending(item => Read(item, operation.Path!), comparer).ToList()
              : source.OrderBy(item => Read(item, operation.Path!), comparer).ToList());
            break;

          case RemoteBatchOperationKind.Count:
            var collection = Collection(Target());
            scope.Results.Add(operation.Id, collection is ICollection counted
              ? counted.Count : EnumerateCollection(collection, operation.Id).Count());
            break;

          case RemoteBatchOperationKind.Take:
            scope.Results.Add(operation.Id,
              EnumerateCollection(Collection(Target()), operation.Id).Take((int)Arguments()[0]!));
            break;

          case RemoteBatchOperationKind.Output:
            outputs.Add(operation.OutputName!,
              serializer.Serialize(Target(), operation.Projection, operation.OutputName!));
            break;

          case RemoteBatchOperationKind.ForEach:
            ExecuteRows(operation, Target(), scope, inputs, serializer, rows);
            break;
        }
      }
      catch (BatchSourceException)
      {
        throw;
      }
      catch (BatchOperationException)
      {
        throw;
      }
      catch (Exception error)
      {
        throw new BatchOperationException(operation.Id, error);
      }
    }
  }

  private static IEnumerable Collection(object? value) =>
    value is IEnumerable collection && value is not string ? collection
      : throw new ArgumentException("Collection operation requires an enumerable source.");

  /// <summary>
  /// Keeps source enumeration failures distinct from errors processing an item.
  /// </summary>
  private static IEnumerable<object?> EnumerateCollection(IEnumerable source, int operationId)
  {
    IEnumerator enumerator;
    try
    {
      enumerator = source.GetEnumerator();
    }
    catch (Exception error)
    {
      throw new BatchSourceException(operationId, error);
    }

    try
    {
      while (true)
      {
        object? item;
        try
        {
          if (!enumerator.MoveNext()) break;
          item = enumerator.Current;
        }
        catch (Exception error)
        {
          throw new BatchSourceException(operationId, error);
        }
        yield return item;
      }
    }
    finally
    {
      try
      {
        (enumerator as IDisposable)?.Dispose();
      }
      catch (Exception error)
      {
        throw new BatchSourceException(operationId, error);
      }
    }
  }

  private List<object> Filter(int operationId, object? source, RemoteBatchPredicate predicate,
    Func<RemoteBatchReference, object?> value)
  {
    var type = predicate.Kind == RemoteBatchPredicateKind.AssignableTo ? Resolve(predicate.Type!) : null;
    var expected = predicate.Value == null ? null : value(predicate.Value);
    var comparison = predicate.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    var result = new List<object>();
    foreach (var item in EnumerateCollection(Collection(source), operationId))
    {
      if (item == null) continue;
      bool matches = predicate.Kind switch
      {
        RemoteBatchPredicateKind.AssignableTo => type!.IsInstanceOfType(item),
        RemoteBatchPredicateKind.EnumName => string.Equals(
          Read(item, predicate.Path!)?.ToString(), (string?)expected, comparison),
        RemoteBatchPredicateKind.StringContains => expected is string text &&
          Read(item, predicate.Path!)?.ToString()?.IndexOf(text, comparison) >= 0,
        RemoteBatchPredicateKind.Comparison => Compare(
          Read(item, predicate.Path!), predicate.OtherPath == null
            ? expected : Read(item, predicate.OtherPath), predicate.Operator),
        _ => throw new ArgumentException("Unknown collection predicate.")
      };
      if (matches) result.Add(item);
    }
    return result;
  }

  private static bool Compare(object? left, object? right, ComparisonOperator comparison)
  {
    int order = Comparer<object?>.Default.Compare(left, right);
    return comparison switch
    {
      ComparisonOperator.Equal => order == 0,
      ComparisonOperator.NotEqual => order != 0,
      ComparisonOperator.GreaterThan => order > 0,
      ComparisonOperator.GreaterThanOrEqual => order >= 0,
      ComparisonOperator.LessThan => order < 0,
      ComparisonOperator.LessThanOrEqual => order <= 0,
      _ => throw new ArgumentException("Unknown comparison operator.")
    };
  }

  private void ExecuteRows(RemoteBatchOperation operation, object? source, Scope parent,
    object?[] inputs, BatchValueSerializer serializer, List<RemoteBatchRow> rows)
  {
    List<object?> selected;
    var indices = new List<int>();

    // Validate every selected index before executing a row body. Otherwise, an
    // invalid selection could run earlier rows before failing at the missing index.
    try
    {
      if (source is not IEnumerable sequence || source is string)
        throw new ArgumentException("ForEach source is not a collection.");
      var selection = operation.Indices == null ? null : new HashSet<int>(operation.Indices);
      selected = new();
      int index = 0;
      foreach (var item in sequence)
      {
        if (selection == null || selection.Contains(index))
        {
          selected.Add(item);
          indices.Add(index);
        }
        index++;
      }
      if (selection != null && selected.Count != selection.Count)
        throw new ArgumentException("Selected index does not exist in the source.");
    }
    catch (Exception error)
    {
      throw new BatchSourceException(operation.Id, error);
    }

    for (int i = 0; i < selected.Count; i++)
    {
      var row = new RemoteBatchRow
      {
        CollectionOperationId = operation.Id,
        SourceIndex = indices[i]
      };
      var scope = new Scope(parent);
      scope.Items.Add(operation.Id, selected[i]);
      try
      {
        ExecuteSequence(operation.Body, scope, inputs, serializer, row.Values, row.Rows);
      }
      catch (BatchSourceException)
      {
        // Enumeration failures abort the request, including nested collections.
        throw;
      }
      catch (BatchOperationException error)
      {
        row.Values.Clear();
        row.Rows.Clear();
        var cause = Unwrap(error.InnerException!);
        row.Error = new()
        {
          OperationId = error.OperationId,
          OutputPath = error.InnerException is BatchValueSerializer.BatchProjectionException projection
            ? projection.Path : null,
          ExceptionType = cause.GetType().FullName!,
          Message = cause.Message,
          StackTrace = cause.StackTrace
        };
      }
      rows.Add(row);
    }
  }

  /// <summary>
  /// Reads a dot-separated property or field path through inherited and nonpublic
  /// members. Reading stops at a null value and returns null. A missing member throws.
  /// An <c>EnumValue</c> segment on an enum returns its underlying numeric value.
  /// </summary>
  public object? Read(object? source, string path)
  {
    foreach (var segment in path.Split('.'))
    {
      if (source == null)
        return null;
      var type = source.GetType();
      if (type.IsEnum && segment == nameof(RemoteBatchEnum.EnumValue))
      {
        source = Convert.ChangeType(source, Enum.GetUnderlyingType(type));
        continue;
      }
      var member = _members.GetOrAdd((type, segment), key =>
      {
        foreach (var candidate in Hierarchy(key.Type))
        {
          var property = candidate.GetProperty(key.Name, Members);
          if (property != null && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null)
            return property;
          var field = candidate.GetField(key.Name, Members);
          if (field != null)
            return field;
        }
        throw new MissingMemberException(type.FullName, segment);
      });
      source = member is PropertyInfo property
        ? property.GetValue(source, null) : ((FieldInfo)member).GetValue(source);
    }
    return source;
  }

  private MethodBase Bind(RemoteBatchMethod descriptor, Type? runtimeType, RemoteBatchOperationKind kind)
  {
    var declaring = Resolve(descriptor.DeclaringType)
      ?? throw new TypeLoadException(descriptor.DeclaringType.FullName);
    if (runtimeType != null && !declaring.IsAssignableFrom(runtimeType))
      throw new ArgumentException("Instance does not implement the declaring type.");
    var parameters = descriptor.ParameterTypes.Select(Resolve).ToArray();
    var genericArguments = descriptor.GenericArguments.Select(Resolve).ToArray();

    // Include assembly identities in the cache key so types with the same name
    // in different assemblies cannot share an overload binding.
    var signature = declaring.AssemblyQualifiedName + "|" + kind + "|" + descriptor.Name + "|" +
      string.Join(";", parameters.Select(t => t.AssemblyQualifiedName)) + "|" +
      string.Join(";", genericArguments.Select(t => t.AssemblyQualifiedName));
    return _methods.GetOrAdd((runtimeType ?? declaring, signature), _ =>
    {
      MethodBase? bound = null;
      if (kind == RemoteBatchOperationKind.Create)
        bound = declaring.GetConstructors(Members & ~BindingFlags.Static).SingleOrDefault(c =>
          c.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
      else
      {
        foreach (var type in Hierarchy(declaring))
        {
          var matches = new List<MethodInfo>();
          foreach (var candidate in type.GetMethods(Members))
          {
            if (candidate.Name != descriptor.Name ||
                candidate.IsStatic != (kind == RemoteBatchOperationKind.CallStatic))
              continue;
            MethodInfo method = candidate;
            if (candidate.IsGenericMethodDefinition)
            {
              if (candidate.GetGenericArguments().Length != genericArguments.Length)
                continue;
              try
              {
                method = candidate.MakeGenericMethod(genericArguments);
              }
              catch (ArgumentException)
              {
                // An overload with incompatible generic constraints cannot match.
                continue;
              }
            }
            else if (genericArguments.Length != 0)
              continue;
            if (method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))
              matches.Add(method);
          }
          if (matches.Count > 1)
            throw new AmbiguousMatchException(descriptor.Name);
          if (matches.Count == 1)
          {
            bound = matches[0];
            break;
          }
        }
      }

      if (bound == null)
        throw new MissingMethodException(declaring.FullName, descriptor.Name);
      if (bound.GetParameters().Any(p => p.ParameterType.IsByRef || p.IsOut) || bound.ContainsGenericParameters)
        throw new NotSupportedException("Batch calls do not support ref/out or open generic signatures.");
      if (bound is MethodInfo methodInfo && (typeof(Task).IsAssignableFrom(methodInfo.ReturnType) ||
          methodInfo.ReturnType.FullName == "System.Threading.Tasks.ValueTask" ||
          methodInfo.ReturnType.FullName?.StartsWith("System.Threading.Tasks.ValueTask`", StringComparison.Ordinal) == true))
        throw new NotSupportedException("Batch calls do not await asynchronous methods.");
      return bound;
    });
  }

  private Type Resolve(RemoteBatchType identity)
  {
    if (identity.ElementType != null)
    {
      var element = Resolve(identity.ElementType);
      if (identity.IsByRef)
        return element.MakeByRefType();
      return identity.IsVector ? element.MakeArrayType() : element.MakeArrayType(identity.ArrayRank);
    }
    var definition = resolveType(identity);
    return identity.GenericArguments.Length == 0 ? definition
      : definition.MakeGenericType(identity.GenericArguments.Select(Resolve).ToArray());
  }

  private static IEnumerable<Type> Hierarchy(Type type)
  {
    for (var current = type; current != null; current = current.BaseType)
      yield return current;
    if (type.IsInterface)
      foreach (var inherited in type.GetInterfaces())
        yield return inherited;
  }

  private static Exception Unwrap(Exception ex)
  {
    while ((ex is TargetInvocationException || ex is BatchValueSerializer.BatchProjectionException) && ex.InnerException != null)
      ex = ex.InnerException;
    return ex;
  }

  private sealed class BatchOperationException(int id, Exception inner)
    : InvalidOperationException($"Batch operation {id} failed: {inner.Message}", inner)
  {
    public int OperationId { get; } = id;
  }

  private sealed class BatchSourceException(int id, Exception inner)
    : InvalidOperationException($"Collection operation {id} failed: {inner.Message}", inner);
}
