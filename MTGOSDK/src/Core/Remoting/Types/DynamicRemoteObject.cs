﻿/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;

using Microsoft.CSharp.RuntimeBinder;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
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
  public class DynamicRemoteMethod : DynamicObject
  {
    private readonly string _name;
    private readonly List<RemoteMethodInfo> _methods;
    private readonly DynamicRemoteObject _parent;
    private readonly Type[] _genericArguments;

    public DynamicRemoteMethod(
      string name,
      DynamicRemoteObject parent,
      List<RemoteMethodInfo> methods,
      Type[] genericArguments = null)
    {
      genericArguments ??= Array.Empty<Type>();

      _name = name;
      _parent = parent;
      _methods = methods;

      _genericArguments = genericArguments;
    }

    public override bool TryInvoke(
      InvokeBinder binder,
      object[] args,
      out object result)
    => TryInvoke(args, out result);

    public bool TryInvoke(object[] args, out object result)
    {
      List<RemoteMethodInfo> overloads = this._methods;

      // Narrow down (hopefully to one) overload with the same amount of types
      // TODO: We COULD possibly check the args types (local ones,
      // RemoteObjects, DynamicObjects, ...) if we still have multiple results
      overloads = overloads
        .Where(overload => overload.GetParameters().Length == args.Length)
        // Concatenate any other matches in case overloads were incorrectly
        // routed due to missing argument types (i.e. null arguments).
        .Concat(this._parent.__type.Methods.Where(m => m.Name == _name))
        .DistinctBy(overload => overload.ToString())
        .ToList();

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
            .Invoke(_parent.__ro, args);
        }
        else
        {
          if (overload.IsGenericMethod)
          {
            throw new ArgumentException("A generic method was initialized with no generic arguments.");
          }
          // OK, invoking without generic arguments
          result = overloads.Single()
            .Invoke(_parent.__ro, args);
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

            Type? argType   = args[i]?.GetType();
            Type? paramType = parameters[i]?.ParameterType;

            // Check assignment if local types, otherwise compare by FullName.
            bool bothLocal = argType.GetType().FullName   == "System.RuntimeType"
                          && paramType.GetType().FullName == "System.RuntimeType";

            bool valid = bothLocal
              ? paramType.IsAssignableFrom(argType)
              : argType.FullName == paramType.FullName;

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
          result = matchingOverloads.Single().Invoke(_parent.__ro, args);
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

    public override bool Equals(object obj)
    {
      return obj is DynamicRemoteMethod method &&
          _name == method._name &&
          EqualityComparer<DynamicRemoteObject>.Default.Equals(_parent, method._parent) &&
          EqualityComparer<Type[]>.Default.Equals(_genericArguments, method._genericArguments);
    }

    public override int GetHashCode()
    {
      int hashCode = -734779080;
      hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(_name);
      hashCode = hashCode * -1521134295 + EqualityComparer<DynamicRemoteObject>.Default.GetHashCode(_parent);
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
      new DynamicRemoteMethod(_name, _parent, _methods,
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
    Type lastType = __type;
    Type nextType = __type;

    // We use this dictionary to make sure overides from subclasses don't get
    // exported twice (for the parent as well)
    var _processedOverloads = new ConcurrentDictionary<string, List<MethodBase>>();
    do
    {
      var members = nextType.GetMembers((BindingFlags)0xffff);
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

  private IEnumerable<MemberInfo> GetMembers()
  {
    if (__membersInner != null && __ongoingMembersDumper == null)
    {
      return __membersInner;
    }
    // Defining a new method so we can use "yield return" (Outer function
    // already returned a "real" IEnumerable in the above case) so using
    // "yield return" as well is forbidden.
    IEnumerable<MemberInfo> Aggregator()
    {
      __membersInner ??= new List<MemberInfo>();
      __ongoingMembersDumper ??= GetAllMembersRecursive();
      __ongoingMembersDumperEnumerator ??= __ongoingMembersDumper.GetEnumerator();
      foreach (var member in __membersInner)
      {
        yield return member;
      }
      while(__ongoingMembersDumperEnumerator.MoveNext())
      {
        var member = __ongoingMembersDumperEnumerator.Current;
        __membersInner.Add(member);
        yield return member;
      }
      __ongoingMembersDumper = null;
      __ongoingMembersDumperEnumerator = null;

    };
    return Aggregator();
  }

  public T InvokeMethod<T>(string name, params object[] args)
  {
    var matchingMethods = from member in __members
        where member.Name == name
        where ((MethodInfo)member).GetParameters().Length == args.Length
        select member;
    return (T)(matchingMethods.Single() as MethodInfo).Invoke(__ro, args);
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
  public override bool TryGetMember(GetMemberBinder binder, out object result)
  {
    try
    {
      object obj = null;
      bool ret = Retry(() =>
      {
        Type lastType = __type;
        Type nextType = __type;
        do
        {
          bool found = TryGetMember(nextType, binder.Name, out obj);
          if (found) return true;
          lastType = nextType;
          nextType = nextType.BaseType;
        }
        while (nextType != null || lastType == typeof(object));
        return false;
      }, raise: true);

      result = obj;
      return ret;
    }
    catch (Exception ex)
    {
      throw new Exception($"DynamicObject threw an exception while trying to get member \"{binder.Name}\" from {__type.Name}", innerException: ex);
    }
  }

  private bool TryGetMember(Type t, string name, out object result)
  {
    result = null;
    MemberInfo[] members = t.GetMembers((BindingFlags)0xffff);
    List<MemberInfo> matches = members
      .Where(member => member.Name == name)
      .ToList();

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
          result = Retry(() => ((FieldInfo)firstMember).GetValue(__ro),
                         delay: 10, raise: true);
        }
        catch (Exception ex)
        {
          throw new Exception($"Field \"{name}\"'s getter threw an exception", innerException: ex);
        }
        break;
      case MemberTypes.Property:
        try
        {
          result = Retry(() => ((PropertyInfo)firstMember).GetValue(__ro),
                         delay: 10, raise: true);
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
      return new DynamicRemoteMethod(name, this, methodGroup);
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
    // If "TryInvokeMember" was called first (instead of "TryGetMember")
    // it means that the user specified generic args (if any are even requied)
    // within '<' and '>' signs or there aren't any generic args. We can just
    // do the call here instead of letting the dynamic runtime resort to
    // calling 'TryGetMember'

    DynamicRemoteMethod drm = GetMethodProxy(binder.Name);
    Type binderType = binder.GetType();
    PropertyInfo TypeArgumentsPropInfo = binderType.GetProperty("TypeArguments");
    if (TypeArgumentsPropInfo != null)
    {
      // We got ourself a binder which implemented .NET's internal
      // "ICSharpInvokeOrInvokeMemberBinder" Interface:
      // https://github.com/microsoft/referencesource/blob/master/Microsoft.CSharp/Microsoft/CSharp/ICSharpInvokeOrInvokeMemberBinder.cs
      //
      // We can now see if the invoked for the function specified generic types
      // In that case, we can hijack and do the call here
      // Otherwise - Just let TryGetMember return a proxy
      if (TypeArgumentsPropInfo.GetValue(binder) is IList<Type> genArgs)
      {
        foreach (Type t in genArgs)
        {
          // Aggregate the generic types into the dynamic remote method
          // Example:
          //  * Invoke method is Insert<,>
          //  * Given types are ['T', 'S']
          //  * First loop iteration: Inert<,> --> Insert<T,>
          //  * Second loop iteration: Inert<T,> --> Insert<T,S>
          drm = drm[t];
        }
      }
    }

    object obj = null;
    bool success;
    try
    {
      success = Retry(() => drm.TryInvoke(args, out obj), raise: true);
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
    result = obj;
    return success;
  }

  public bool HasMember(string name) =>
    __members.Any(member => member.Name == name);
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
          ((FieldInfo)firstMember).SetValue(__ro, value);
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
          ((PropertyInfo)firstMember).SetValue(__ro, value);
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
    var binder = Binder.GetMember(CSharpBinderFlags.None, memberName, obj.GetType(),
      new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
    var callsite = CallSite<Func<CallSite, object, object>>.Create(binder);
    if (obj is DynamicRemoteObject dro)
    {
      if (dro.HasMember(memberName))
      {
        if (dro.TryGetMember(binder as GetMemberBinder, out output))
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
      ObjectOrRemoteAddress ooraKey = RemoteFunctionsInvokeHelper.CreateRemoteParameter(key);
      ObjectOrRemoteAddress item = __ro.GetItem(ooraKey);
      if (item.IsNull)
      {
        return null;
      }
      else if (item.IsRemoteAddress)
      {
        var remoteObject = this.__ra.GetRemoteObject(item.RemoteAddress, item.Type);
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
    if (!__members.Any(member => member.Name == nameof(GetEnumerator)))
      throw new Exception($"No method called {nameof(GetEnumerator)} found. The remote object probably doesn't implement IEnumerable");

    try
    {
      dynamic enumeratorDro = Retry(() => InvokeMethod<object>(nameof(GetEnumerator)), raise: true);
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
