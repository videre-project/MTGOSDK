/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Microsoft.CSharp.RuntimeBinder;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.Core.Remoting.Types;

/// <summary>
/// A proxy of a remote object.
/// Usages of this class should be strictly as a `dynamic` variable.
/// Field/Property reads/writes are redirect to reading/writing to the fields of
/// the remote object, and method calls are redirected to functions calls in the
/// remote process on the remote object.
/// </summary>
[DebuggerDisplay("Dynamic Proxy of {" + nameof(__ro) + "}")]
public class DynamicRemoteObject : DynamicObject, IEnumerable
{
  // Static caches to speed up reflection-heavy hot paths. Safe across instances.
  private static readonly ConcurrentDictionary<Type, MemberInfo[]> s_membersByType = new(TypeReferenceEqualityComparer.Instance);
  private static readonly ConcurrentDictionary<Type, Dictionary<string, List<MemberInfo>>> s_membersByTypeAndName = new(TypeReferenceEqualityComparer.Instance);
  private static readonly ConcurrentDictionary<Type, HashSet<string>> s_memberNamesByType = new(TypeReferenceEqualityComparer.Instance);
  private static readonly ConcurrentDictionary<Type, PropertyInfo?> s_binderTypeArgsProp = new();
  private static readonly ConcurrentDictionary<(Type ObjType, string MemberName), CallSite<Func<CallSite, object, object>>> s_getMemberCallSites = new();

  // Static cache for DynamicRemoteMethod to avoid recreating on repeated calls
  // Keyed by (typeFullName, methodName) - using FullName string to avoid RemoteType.GetHashCode issues
  private static readonly ConcurrentDictionary<(string, string), DynamicRemoteMethod> s_methodProxyCache = new();

  public class DynamicRemoteMethod : DynamicObject
  {
    private readonly string _name;
    private readonly List<RemoteMethodInfo> _methods;
    private readonly Type[] _genericArguments;
    // Pre-group overloads by arity to avoid repeated filtering.
    private readonly Dictionary<int, List<RemoteMethodInfo>> _overloadsByArity;

    // Parent is now passed at invoke time instead of being stored
    // This enables static caching across all instances of the same type

    public DynamicRemoteMethod(
      string name,
      List<RemoteMethodInfo> methods,
      Type[] genericArguments = null)
    {
      genericArguments ??= Array.Empty<Type>();

      _name = name;
      _methods = methods;

      _genericArguments = genericArguments;

      // Build arity map once.
      _overloadsByArity = new Dictionary<int, List<RemoteMethodInfo>>();
      foreach (var m in _methods)
      {
        int arity = m.GetParameters().Length;
        if (!_overloadsByArity.TryGetValue(arity, out var list))
        {
          list = new List<RemoteMethodInfo>();
          _overloadsByArity[arity] = list;
        }
        list.Add(m);
      }
    }

    // Note: TryInvoke from DynamicObject base class cannot be overridden to add parameters
    // So we provide a public method that takes the parent

    public bool TryInvoke(DynamicRemoteObject parent, object[] args, out object result)
    {
      // Start from pre-grouped arity bucket to reduce work.
      List<RemoteMethodInfo> overloads = _overloadsByArity.TryGetValue(args?.Length ?? 0, out var byArity)
        ? byArity
        : new List<RemoteMethodInfo>(0);

      // In case additional overloads might exist (defensive), union with the
      // type-level methods of same name.
      // This is cheap if cached at the RemoteType level internally.
      // Note: __type.Methods is expected to include all overloads for this type
      if (overloads.Count != 1)
      {
        var extras = parent.__type.Methods.Where(m => m.Name == _name && m.GetParameters().Length == (args?.Length ?? 0));
        if (extras.Any())
        {
          overloads = overloads
            .Concat(extras)
            .DistinctBy(o => o.ToString())
            .ToList();
        }
      }

      if (overloads.Count == 1)
      {
        // Easy case - a unique function name so we can just return it.
        RemoteMethodInfo overload = overloads.Single();
        if (_genericArguments != null && _genericArguments.Any())
        {
          if (!overload.IsGenericMethod)
          {
            throw new ArgumentException("A non-generic method was initialized with some generic arguments.");
          }
          else if (overload.IsGenericMethod &&
                   overload.GetGenericArguments().Length != _genericArguments.Length)
          {
            throw new ArgumentException("Generic method was initialized with the wrong number of generic arguments.");
          }

          // OK, invoking with generic arguments
          result = overload
            .MakeGenericMethod(_genericArguments)
            .Invoke(parent.__ro, args);
        }
        else
        {
          if (overload.IsGenericMethod)
          {
            throw new ArgumentException("A generic method was initialized with no generic arguments.");
          }
          // OK, invoking without generic arguments
          result = overloads.Single()
            .Invoke(parent.__ro, args);
        }
      }
      else if (overloads.Count > 1)
      {
        List<RemoteMethodInfo> matchingOverloads = new();
        foreach (var overload in overloads)
        {
          var parameters = overload.GetParameters();
          if (parameters.Length != args.Length)
            continue;

          bool isMatch = true;
          for (int i = 0; i < parameters.Length; i++)
          {
            // If the argument provided is null, we cannot deduce a type match.
            if (args[i] == null) continue;

            Type? argType = args[i]?.GetType();
            Type? paramType = parameters[i]?.ParameterType;

            // Check assignment if local types, otherwise check inheritance for remote types.
            bool bothLocal = argType.GetType().FullName == "System.RuntimeType"
                          && paramType.GetType().FullName == "System.RuntimeType";

            bool valid;
            if (bothLocal)
            {
              valid = paramType.IsAssignableFrom(argType);
            }
            else
            {
              // For remote types, check exact match first
              if (argType.FullName == paramType.FullName)
              {
                valid = true;
              }
              else
              {
                // Check if argType implements paramType (interface inheritance)
                // Walk the interface list and base types of argType
                valid = IsRemoteTypeAssignableTo(argType, paramType);
              }
            }

            if (!valid)
            {
              isMatch = false;
              break;
            }
          }

          if (isMatch)
            matchingOverloads.Add(overload);
        }

        if (matchingOverloads.Count == 1)
        {
          // We found a single matching overload, so we can invoke it.
          result = matchingOverloads.Single().Invoke(parent.__ro, args);
        }
        else
        {
          // We have multiple matching overloads, so we need to throw an exception.
          throw new ArgumentException(
            $"Multiple overloads found for method '{_name}' with {args.Length} parameters. " +
            $"Please specify the generic arguments to disambiguate.");
        }
      }
      else // This case is for "overloads.Count == 0"
      {
        throw new ArgumentException(
          $"No overloads were found for method '{_name}' with {args.Length} " +
           "matching parameters. This likely means that one or more of the " +
           "arguments were passed with the wrong type (or in the wrong order).");
      }
      return true;
    }

    /// <summary>
    /// Checks if a remote type is assignable to another remote type by walking
    /// the interface list and base type hierarchy.
    /// </summary>
    private static bool IsRemoteTypeAssignableTo(Type argType, Type paramType)
    {
      if (argType == null || paramType == null)
        return false;

      string targetFullName = paramType.FullName;

      // Check implemented interfaces
      try
      {
        var interfaces = argType.GetInterfaces();
        foreach (var iface in interfaces)
        {
          if (iface.FullName == targetFullName)
            return true;
        }
      }
      catch { /* Ignore if GetInterfaces fails */ }

      // Walk base type hierarchy
      Type? currentType = argType.BaseType;
      while (currentType != null && currentType != typeof(object))
      {
        if (currentType.FullName == targetFullName)
          return true;

        // Also check interfaces of base types
        try
        {
          var baseInterfaces = currentType.GetInterfaces();
          foreach (var iface in baseInterfaces)
          {
            if (iface.FullName == targetFullName)
              return true;
          }
        }
        catch { /* Ignore if GetInterfaces fails */ }

        currentType = currentType.BaseType;
      }

      return false;
    }

    public override bool Equals(object obj)
    {
      return obj is DynamicRemoteMethod method &&
          _name == method._name &&
          EqualityComparer<Type[]>.Default.Equals(_genericArguments, method._genericArguments);
    }

    public override int GetHashCode()
    {
      int hashCode = -734779080;
      hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(_name);
      hashCode = hashCode * -1521134295 + EqualityComparer<Type[]>.Default.GetHashCode(_genericArguments);
      return hashCode;
    }

    // Functions to turn our base method into a "generic" one - with specific
    // arguments instead of generic placeholders. I wish I could've overridden
    // the 'MyFunc<T>' notation but I don't think that syntax is modifiable in
    // C#.
    //
    // Instead we go to the second best solution which is use indexers:
    // MyFunc[typeof(T)]
    // - or -
    // Type t = typeof(T);
    // MyFunc[t]
    //
    // Since some methods support multiple generic arguments I also overrode
    // some multi-dimensional indexers below. This allows that to compile:
    // Type t,p,q = ...;
    // MyOtherFunc[t,p,q]

    public DynamicRemoteMethod this[Type t] =>
      new DynamicRemoteMethod(_name, _methods,
          _genericArguments.Concat(new Type[] { t }).ToArray());

    public DynamicRemoteMethod this[Type t1, Type t2] =>
      this[t1][t2];
    public DynamicRemoteMethod this[Type t1, Type t2, Type t3] =>
      this[t1, t2][t3];
    public DynamicRemoteMethod this[Type t1, Type t2, Type t3, Type t4] =>
      this[t1, t2, t3][t4];
    public DynamicRemoteMethod this[Type t1, Type t2, Type t3, Type t4, Type t5] =>
      this[t1, t2, t3, t4][t5];
  }

  public virtual RemoteHandle __ra { get => m_ra; set => m_ra = value; }
  private RemoteHandle m_ra = null!;

  public virtual RemoteObject __ro { get => m_ro; set => m_ro = value; }
  private RemoteObject m_ro = null!;

  public virtual RemoteType __type { get => m_type; set => m_type = value; }
  private RemoteType m_type = null!;

  public virtual DateTime __timestamp { get => m_timestamp; set => m_timestamp = value; }
  private DateTime m_timestamp = DateTime.Now;

  private IEnumerable<MemberInfo> __ongoingMembersDumper = null;
  private IEnumerator<MemberInfo> __ongoingMembersDumperEnumerator = null;
  private List<MemberInfo> __membersInner = null;
  private readonly object __membersLock = new();
  public IEnumerable<MemberInfo> __members => GetMembers();

  public DynamicRemoteObject(RemoteHandle ra, RemoteObject ro)
  {
    __ra = ra;
    __ro = ro;
    __type = ro.GetType() as RemoteType;
    __timestamp = DateTime.Now;
    if (__type == null && ro.GetType() != null)
    {
      throw new ArgumentException("Can only create DynamicRemoteObjects of RemoteObjects with Remote Types. (As returned from GetType())");
    }
  }

  public DynamicRemoteObject() { } // For avoiding overriding reference type

  ~DynamicRemoteObject()
  {
    // Destructor - we need to make sure we don't leave dangling references to
    // remote objects. This is important because the remote object might be
    // disposed of and we don't want to keep a reference to it.
    if (__ro != null && __ro.IsValid)
    {
      __ro.ReleaseReference();
    }

    __ro = null;
    __ra = null;
    __type = null;
  }

  /// <summary>
  /// Gets the type of the proxied remote object, in the remote app.
  /// </summary>
  public new Type GetType() => __type;

  /// <summary>
  /// Processes the result from a direct Communicator invocation.
  /// Handles void returns, null, primitives, and remote objects.
  /// </summary>
  private object ProcessInvocationResult(InvocationResults invokeRes)
  {
    if (invokeRes == null || invokeRes.VoidReturnType)
      return null;

    var oora = invokeRes.ReturnedObjectOrAddress;
    if (oora == null || oora.IsNull)
      return null;

    if (!oora.IsRemoteAddress)
      return PrimitivesEncoder.Decode(oora);

    // Remote object - wrap in DynamicRemoteObject using lightweight path
    // /get_field already pinned the object, so we skip /object call
    RemoteObject ro = __ra.GetRemoteObjectFromField(oora.RemoteAddress, oora.Type);
    dynamic dro = ro.Dynamify();
    dro.__timestamp = oora.Timestamp;
    return dro;
  }

  /// <summary>
  /// Encodes parameters for direct Communicator invocation.
  /// </summary>
  private ObjectOrRemoteAddress[] EncodeParameters(object[] args)
  {
    if (args == null || args.Length == 0)
      return Array.Empty<ObjectOrRemoteAddress>();

    var result = new ObjectOrRemoteAddress[args.Length];
    for (int i = 0; i < args.Length; i++)
      result[i] = RemoteFunctionsInvokeHelper.CreateRemoteParameter(args[i]);
    return result;
  }

  /// <summary>
  /// Indicates whether this proxy should be treated as "nullish" for boolean checks.
  /// Returns true when the RemoteClient is not initialized/disposed, or when the
  /// underlying RemoteObject is null/invalid.
  /// </summary>
  public bool __isNullish =>
    !RemoteClient.IsInitialized || __ro is null || !__ro.IsValid;

  /// <summary>
  /// Defines truthiness for DynamicRemoteObject. Evaluates to false when
  /// RemoteClient is disposed (not initialized) or the underlying RemoteObject
  /// is null/invalid; true otherwise.
  /// </summary>
  public static bool operator true(DynamicRemoteObject value)
    => value is not null &&
       RemoteClient.IsInitialized &&
       value.__ro is not null && value.__ro.IsValid;

  /// <summary>
  /// Logical false operator paired with operator true.
  /// </summary>
  public static bool operator false(DynamicRemoteObject value)
    => !(value is not null &&
         RemoteClient.IsInitialized &&
         value.__ro is not null && value.__ro.IsValid);

  private IEnumerable<MemberInfo> GetAllMembersRecursive()
  {
    if (__type == null)
      yield break;
    Type lastType = __type;
    Type nextType = __type;

    // We use this dictionary to make sure overides from subclasses don't get
    // exported twice (for the parent as well)
    var _processedOverloads = new ConcurrentDictionary<string, List<MethodBase>>();
    do
    {
      if (nextType == null)
        yield break;
      var members = nextType.GetMembers((BindingFlags) 0xffff);
      foreach (MemberInfo member in members)
      {
        if (member is MethodBase newMethods)
        {
          if (!_processedOverloads.TryGetValue(member.Name, out _))
          {
            _processedOverloads[member.Name] = new List<MethodBase>();
          }
          List<MethodBase> oldMethods = _processedOverloads[member.Name];

          bool overridden = oldMethods.Any(oldMethod => oldMethod.SignatureEquals(newMethods));
          if (overridden)
          {
            continue;
          }

          _processedOverloads[member.Name].Add(newMethods);
        }
        yield return member;
      }
      lastType = nextType;
      nextType = nextType.BaseType;
    }
    while (nextType != null && lastType != typeof(object));
  }

  // Cached per-type reflection helpers
  private static MemberInfo[] GetCachedMembers(Type t)
  {
    return s_membersByType.GetOrAdd(t, static type => type.GetMembers((BindingFlags) 0xffff));
  }

  private static Dictionary<string, List<MemberInfo>> GetCachedMembersByNameMap(Type t)
  {
    return s_membersByTypeAndName.GetOrAdd(t, static type =>
    {
      var dict = new Dictionary<string, List<MemberInfo>>(StringComparer.Ordinal);
      foreach (var m in GetCachedMembers(type))
      {
        if (!dict.TryGetValue(m.Name, out var list))
        {
          list = new List<MemberInfo>();
          dict[m.Name] = list;
        }
        list.Add(m);
      }
      return dict;
    });
  }

  private static HashSet<string> GetCachedMemberNames(Type t)
  {
    return s_memberNamesByType.GetOrAdd(t, static type =>
    {
      var set = new HashSet<string>(StringComparer.Ordinal);
      foreach (var m in GetCachedMembers(type))
        set.Add(m.Name);
      return set;
    });
  }

  private IEnumerable<MemberInfo> GetMembers()
  {
    // Fast path: if enumeration is complete, return the cached list directly
    // (no lock needed for reads once __ongoingMembersDumper is null)
    if (__membersInner != null && __ongoingMembersDumper == null)
    {
      return __membersInner;
    }

    // Thread-safe path: complete enumeration under lock and return snapshot
    lock (__membersLock)
    {
      // Double-check after acquiring lock
      if (__membersInner != null && __ongoingMembersDumper == null)
      {
        return __membersInner;
      }

      // Initialize if needed
      __membersInner ??= new List<MemberInfo>();
      __ongoingMembersDumper ??= GetAllMembersRecursive();
      __ongoingMembersDumperEnumerator ??= __ongoingMembersDumper.GetEnumerator();

      // Complete the entire enumeration under lock
      while (__ongoingMembersDumperEnumerator.MoveNext())
      {
        var member = __ongoingMembersDumperEnumerator.Current;
        __membersInner.Add(member);
      }

      // Mark enumeration as complete
      __ongoingMembersDumper = null;
      __ongoingMembersDumperEnumerator = null;

      return __membersInner;
    }
  }

  public T InvokeMethod<T>(string name, params object[] args)
  {
    var matchingMethods = from member in __members
                          where member.Name == name
                          where ((MethodInfo) member).GetParameters().Length == args.Length
                          select member;
    return (T) (matchingMethods.Single() as MethodInfo).Invoke(__ro, args);
  }

  /// <summary>
  /// Enabling 'await' support for DynamicRemoteObjects that wrap System.Threading.Tasks.Task.
  /// </summary>
  public TaskAwaiter<object?> GetAwaiter()
  {
    if (!IsTaskType(__type))
    {
       throw new InvalidOperationException($"Remote object of type {__type?.FullName ?? "null"} is not a Task and cannot be awaited directly.");
    }

    return WaitRemoteTaskAsync().GetAwaiter();
  }

  private bool IsTaskType(Type t)
  {
     if (t == null) return false;
     // Check base types
     Type curr = t;
     while (curr != null) {
        if (curr.FullName != null && curr.FullName.StartsWith("System.Threading.Tasks.Task")) return true;
        curr = curr.BaseType;
     }
     return false;
  }

  private async Task<object?> WaitRemoteTaskAsync()
  {
    // Poll IsCompleted
    int delay = 5;
    while(true)
    {
      var invokeResult = __ra.Communicator.InvokeMethod(
        __ro.RemoteToken,
        __type.FullName,
        "get_IsCompleted",
        Array.Empty<string>(), 
        Array.Empty<ObjectOrRemoteAddress>()); 
      
      bool isCompleted = (bool)ProcessInvocationResult(invokeResult);
      
      if (isCompleted) break;
      
      await Task.Delay(delay);
      if (delay < 50) delay += 5;
    }

    // Check IsFaulted
    var faultedRes = __ra.Communicator.InvokeMethod(
      __ro.RemoteToken,
      __type.FullName,
      "get_IsFaulted",
      Array.Empty<string>(),
      Array.Empty<ObjectOrRemoteAddress>());
    bool isFaulted = (bool)ProcessInvocationResult(faultedRes);

    if (isFaulted)
    {
      // Get Exception
      var exRes = __ra.Communicator.InvokeMethod(
        __ro.RemoteToken,
        __type.FullName,
        "get_Exception",
        Array.Empty<string>(),
        Array.Empty<ObjectOrRemoteAddress>());
      
      dynamic remoteEx = ProcessInvocationResult(exRes);
      // Try to get message from exception
      string exMsg = "Unknown Remote Exception";
      try { exMsg = remoteEx.ToString(); } catch {}
      
      throw new Exception($"Remote Task Faulted: {exMsg}");
    }
    
    // Check for Result property (if Task<T>)
    if (HasMember("Result"))
    {
      var resultRes = __ra.Communicator.InvokeMethod(
        __ro.RemoteToken,
        __type.FullName,
        "get_Result",
        Array.Empty<string>(),
        Array.Empty<ObjectOrRemoteAddress>());
      return ProcessInvocationResult(resultRes);
    }
    
    return null;
  }

  #region Dynamic Object API
  public override bool TryUnaryOperation(UnaryOperationBinder binder, out object result)
  {
    // Override truthiness checks when the RemoteClient is not intialized.
    if (!RemoteClient.IsInitialized)
    {
      if (binder.Operation == ExpressionType.IsTrue)
      {
        result = false;
        return true;
      }
      if (binder.Operation == ExpressionType.IsFalse)
      {
        result = true;
        return true;
      }
    }

    // Otherwise, defer to base for normal dynamic semantics
    return base.TryUnaryOperation(binder, out result);
  }
  private static readonly ActivitySource s_activitySource = new("MTGOSDK.Core");

  public override bool TryGetMember(GetMemberBinder binder, out object result)
  {
    using var activity = s_activitySource.StartActivity("DRO.GetMember");
    activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
    activity?.SetTag("member", binder.Name);

    try
    {
      object obj = null;
      bool ret = Retry(() =>
      {
        for (Type t = __type; t != null && t != typeof(object); t = t.BaseType)
        {
          bool found = TryGetMember(t, binder.Name, out obj);
          if (found) return true;
        }
        return false;
      }, raise: true);

      result = obj;
      return ret;
    }
    catch (Exception ex)
    {
      activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
      throw new Exception($"DynamicObject threw an exception while trying to get member \"{binder.Name}\" from {__type.Name}", innerException: ex);
    }
  }

  private bool TryGetMember(Type t, string name, out object result)
  {
    result = null;
    if (t == null)
    {
      return false;
    }

    // Use cached member list per type and pre-bucketed name map
    var byName = GetCachedMembersByNameMap(t);

    List<MemberInfo> matches = byName.TryGetValue(name, out var list)
      ? list
      : new List<MemberInfo>(0);

    if (!matches.Any())
    {
      result = null;
      return false;
    }

    // At least 1 member with that name
    MemberInfo firstMember = matches[0];
    MemberTypes type = firstMember.MemberType;
    bool singleMatch = matches.Count == 1;

    switch (type)
    {
      case MemberTypes.Field:
        try
        {
          // Fast path: call Communicator directly, bypassing RemoteFieldInfo indirection
          var fieldResult = __ra.Communicator.GetField(
            __ro.RemoteToken,
            t.FullName,
            name);
          result = ProcessInvocationResult(fieldResult);
        }
        catch (Exception ex)
        {
          throw new Exception($"Field \"{name}\"'s getter threw an exception", innerException: ex);
        }
        break;
      case MemberTypes.Property:
        try
        {
          // Fast path: call Communicator directly, bypassing RemotePropertyInfo/RemoteMethodInfo indirection
          var propResult = __ra.Communicator.InvokeMethod(
            __ro.RemoteToken,
            t.FullName,
            $"get_{name}",
            Array.Empty<string>());
          result = ProcessInvocationResult(propResult);
        }
        catch (Exception ex)
        {
          throw new Exception($"Property \"{name}\"'s getter threw an exception", innerException: ex);
        }
        break;
      case MemberTypes.Method:
        // The cases that get here are when the user is trying to:
        // 1. Save a method in a variable:
        //      var methodGroup = dro.Insert;
        // 2. The user is trying to use the "RemoteNET way" of specifing generic:
        //      Type t = typeof(SomeType);
        //      dro.Insert[t]();
        result = GetMethodProxy(name);
        break;
      case MemberTypes.Event:
        // TODO:
        throw new NotImplementedException("Cannot hook to remote events yet.");
      default:
        throw new Exception($"No such member \"{name}\"");
    }
    return true;
  }

  private DynamicRemoteMethod GetMethodProxy(string name)
  {
    // Check static cache first - keyed by (typeFullName, methodName)
    // Skip cache if __type is null (can happen with parameterless constructor)
    var typeFullName = __type?.FullName;
    var cacheKey = typeFullName != null ? (typeFullName, name) : default;
    if (typeFullName != null && s_methodProxyCache.TryGetValue(cacheKey, out var cached))
      return cached;

    var methods = __members.Where(member => member.Name == name).ToArray();
    if (methods.Length == 0)
    {
      throw new Exception($"Method \"{name}\" wasn't found in the members of type {__type.Name}.");
    }

    if (methods.Any(member => member.MemberType != MemberTypes.Method))
    {
      throw new Exception($"A member called \"{name}\" exists in the type and it isn't a method (It's a {methods.First(m => m.MemberType != MemberTypes.Method).MemberType})");
    }
    if (methods.Any(member => !(member is RemoteMethodInfo)))
    {
      throw new Exception($"A method overload for \"{name}\" wasn't a MethodInfo");
    }

    List<RemoteMethodInfo> methodGroup = new();
    methodGroup.AddRange(methods.Cast<RemoteMethodInfo>());
    try
    {
      var result = new DynamicRemoteMethod(name, methodGroup);
      if (typeFullName != null)
        s_methodProxyCache[cacheKey] = result;
      return result;
    }
    catch (Exception ex)
    {
      throw new Exception($"Constructing {nameof(DynamicRemoteMethod)} of \"{name}\" threw an exception", innerException: ex);
    }
  }

  public override bool TryInvokeMember(
    InvokeMemberBinder binder,
    object[] args,
    out object result)
  {
    try
    {
      // Extract generic type arguments from binder if present
      string[] genericArgsFullNames = Array.Empty<string>();
      Type binderType = binder.GetType();
      PropertyInfo TypeArgumentsPropInfo = s_binderTypeArgsProp.GetOrAdd(binderType, static bt => bt.GetProperty("TypeArguments"));
      if (TypeArgumentsPropInfo != null)
      {
        if (TypeArgumentsPropInfo.GetValue(binder) is IList<Type> genArgs && genArgs.Count > 0)
        {
          genericArgsFullNames = genArgs.Select(t => t.FullName).ToArray();
        }
      }

      // Fast path: call Communicator directly, bypassing DynamicRemoteMethod/RemoteMethodInfo
      var encodedArgs = EncodeParameters(args);
      var invokeResult = __ra.Communicator.InvokeMethod(
        __ro.RemoteToken,
        __type.FullName,
        binder.Name,
        genericArgsFullNames,
        encodedArgs);
      result = ProcessInvocationResult(invokeResult);
      return true;
    }
    catch (NullReferenceException ex)
    {
      throw new InvalidOperationException(
        $"Unable to invoke member \"{binder.Name}\" on type {this.__type.Name}. " +
        "The remote object may have been disposed of or is in an invalid state.",
        ex);
    }
    catch (Exception ex)
    {
      throw new Exception(
        $"DynamicObject threw an exception while trying to invoke member \"{binder.Name}\"",
        ex);
    }
  }

  public bool HasMember(string name) =>
    HasMemberCached(name);

  private bool HasMemberCached(string name)
  {
    // Walk the hierarchy and consult the cached name sets.
    for (Type t = __type; t != null && t != typeof(object); t = t.BaseType)
    {
      if (GetCachedMemberNames(t).Contains(name))
        return true;
    }
    return false;
  }
  public override bool TrySetMember(SetMemberBinder binder, object value)
  {
    List<MemberInfo> matches = __members
      .Where(member => member.Name == binder.Name)
      .ToList();

    if (!matches.Any())
    {
      return false;
    }

    // At least 1 member with that name
    MemberInfo firstMember = matches[0];
    MemberTypes type = firstMember.MemberType;
    bool singleMatch = matches.Count == 1;

    // In case we are resolving a field or property
    switch (type)
    {
      case MemberTypes.Field:
        // if (!singleMatch)
        // {
        //   throw new ArgumentException($"Multiple members were found for the name `{binder.Name}` and at least one of them was a field");
        // }
        try
        {
          ((FieldInfo) firstMember).SetValue(__ro, value);
        }
        catch (Exception ex)
        {
          throw new Exception($"Field \"{binder.Name}\"'s getter threw an exception", innerException: ex);
        }
        break;
      case MemberTypes.Property:
        // if (!singleMatch)
        // {
        //   throw new ArgumentException($"Multiple members were found for the name `{binder.Name}` and at least one of them was a property");
        // }
        try
        {
          ((PropertyInfo) firstMember).SetValue(__ro, value);
        }
        catch (Exception ex)
        {
          throw new Exception($"Property \"{binder.Name}\"'s getter threw an exception", innerException: ex);
        }
        break;
      case MemberTypes.Method:
        throw new Exception("Can't modifying method members.");
      case MemberTypes.Event:
        // TODO:
        throw new NotImplementedException("Cannot hook to remote events yet.");
      default:
        throw new Exception($"No such member \"{binder.Name}\".");
    }
    return true;
  }

  /// <summary>
  /// Helper function to access the member <paramref name="memberName"/> of the object <paramref name="obj"/>.
  /// This is equivilent to explicitly compiling the expression '<paramref name="obj"/>.<paramref name="memberName"/>'.
  /// </summary>
  public static bool TryGetDynamicMember(object obj, string memberName, out object output)
  {
    var objType = obj?.GetType() ?? typeof(object);
    var callsite = s_getMemberCallSites.GetOrAdd((objType, memberName), static key =>
    {
      var (type, name) = key;
      var binderLocal = Binder.GetMember(CSharpBinderFlags.None, name, type,
        new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
      return CallSite<Func<CallSite, object, object>>.Create(binderLocal);
    });
    if (obj is DynamicRemoteObject dro)
    {
      if (dro.HasMember(memberName))
      {
        // Build a temporary binder compatible with TryGetMember path
        var binderLocal = Binder.GetMember(CSharpBinderFlags.None, memberName, obj.GetType(),
          new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
        if (dro.TryGetMember(binderLocal as GetMemberBinder, out output))
        {
          return true;
        }
      }
    }

    // Fallback? Does it always just result in TryGetMember?
    try
    {
      output = callsite.Target(callsite, obj);
      return true;
    }
    catch
    {
      output = null;
      return false;
    }
  }
  #endregion

  #region ToString / GetHashCode / Equals

  public override string ToString() => InvokeMethod<string>(nameof(ToString));

  public override int GetHashCode() => InvokeMethod<int>(nameof(GetHashCode));

  public override bool Equals(object obj) => InvokeMethod<bool>(nameof(Equals), obj);
  #endregion

  /// <summary>
  /// Array access. Key can be any primitive / RemoteObject / DynamicRemoteObject
  /// </summary>
  public dynamic this[object key]
  {
    get
    {
      // Fast path: call Communicator directly
      var ooraKey = RemoteFunctionsInvokeHelper.CreateRemoteParameter(key);
      var item = __ra.Communicator.GetItem(__ro.RemoteToken, ooraKey);
      if (item.IsNull)
      {
        return null;
      }
      else if (item.IsRemoteAddress)
      {
        var remoteObject = __ra.GetRemoteObjectFromField(item.RemoteAddress, item.Type);
        dynamic dro = remoteObject.Dynamify();
        dro.__timestamp = item.Timestamp;
        return dro;
      }
      else
      {
        return PrimitivesEncoder.Decode(item.EncodedObject, item.Type);
      }
    }
    set => throw new NotImplementedException();
  }

  public IEnumerator GetEnumerator()
  {
    if (!HasMemberCached(nameof(GetEnumerator)))
      throw new Exception($"No method called {nameof(GetEnumerator)} found. The remote object probably doesn't implement IEnumerable");

    try
    {
      // Fast path: call Communicator directly
      var invokeResult = __ra.Communicator.InvokeMethod(
        __ro.RemoteToken,
        __type.FullName,
        nameof(GetEnumerator),
        Array.Empty<string>());
      dynamic enumeratorDro = ProcessInvocationResult(invokeResult);
      return new DynamicRemoteEnumerator(enumeratorDro);
    }
    catch (NullReferenceException ex)
    {
      throw new InvalidOperationException(
        "Unable to enumerate the remote object. " +
        "The enumerator may have been disposed of or is in an invalid state.",
        ex);
    }
  }
}
