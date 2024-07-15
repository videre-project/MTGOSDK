/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;

using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Reflection;


namespace MTGOSDK.Core.Remoting.Types;

public class RemoteType : TypeStub
{
  private readonly List<RemoteConstructorInfo> _ctors = new List<RemoteConstructorInfo>();
  private readonly List<RemoteMethodInfo> _methods = new List<RemoteMethodInfo>();
  private readonly List<RemoteFieldInfo> _fields = new List<RemoteFieldInfo>();
  private readonly List<RemotePropertyInfo> _properties = new List<RemotePropertyInfo>();
  private readonly List<RemoteEventInfo> _events = new List<RemoteEventInfo>();
  private readonly bool _isArray;
  private readonly bool _isGenericParameter;

  public RemoteHandle App;

  public override bool IsGenericParameter => _isGenericParameter;

  private Lazy<Type> _parent;
  public override Type BaseType => _parent?.Value;

  public RemoteType(RemoteHandle app, Type localType)
      : this(app,
             localType.FullName,
             localType.Assembly.GetName().Name,
             localType.IsArray,
             localType.IsGenericParameter)
  {
    if (localType is RemoteType)
    {
      throw new ArgumentException("This constructor of RemoteType is designed to copy a LOCAL Type object. A RemoteType object was provided instead.");
    }

    // TODO: This ctor is experimentatl because it makes a LOT of assumptions.
    // Most notably the RemoteXXXInfo objects freely use mi's,ci's,pi's (etc)
    // "ReturnType","FieldType","PropertyType"
    // not checking if they are actually RemoteTypes themselves...

    foreach (MethodInfo mi in localType.GetMethods())
    {
      AddMethod(new RemoteMethodInfo(this, mi));
    }
    foreach (ConstructorInfo ci in localType.GetConstructors())
    {
      AddConstructor(new RemoteConstructorInfo(this, ci));
    }
    foreach (PropertyInfo pi in localType.GetProperties())
    {
      RemotePropertyInfo remotePropInfo = new RemotePropertyInfo(this, pi);
      remotePropInfo.RemoteGetMethod = _methods.FirstOrDefault(m => m.Name == "get_" + pi.Name);
      remotePropInfo.RemoteSetMethod = _methods.FirstOrDefault(m => m.Name == "set_" + pi.Name);
      AddProperty(remotePropInfo);
    }
    foreach (FieldInfo fi in localType.GetFields())
    {
      AddField(new RemoteFieldInfo(this, fi));
    }
    foreach (EventInfo ei in localType.GetEvents())
    {
      AddEvent(new RemoteEventInfo(this, ei));
    }
  }

  public RemoteType(
    RemoteHandle app,
    string fullName,
    string assemblyName,
    bool isArray,
    bool isGenericParameter = false)
      : base(name: GetNameFromFullName(fullName),
             assembly: new RemoteAssembly(assemblyName),
             fullName: fullName)
  {
    App = app;
    _isGenericParameter = isGenericParameter;
    _isArray = isArray;
  }

  private static string GetNameFromFullName(string fullName)
  {
    // Deriving name from full name
    string name = fullName.Substring(fullName.LastIndexOf('.') + 1);
    if (fullName.Contains("`"))
    {
      // Generic. Need to cut differently
      string outterTypeFullName = fullName.Substring(0, fullName.IndexOf('`'));
      name = outterTypeFullName.Substring(outterTypeFullName.LastIndexOf('.') + 1);
    }

    return name;
  }

  public void AddConstructor(RemoteConstructorInfo rci) =>
    _ctors.Add(rci);

  public void AddMethod(RemoteMethodInfo rmi) =>
    _methods.Add(rmi);

  public void AddField(RemoteFieldInfo fieldInfo) =>
    _fields.Add(fieldInfo);

  public void AddProperty(RemotePropertyInfo fieldInfo) =>
    _properties.Add(fieldInfo);

  public void AddEvent(RemoteEventInfo eventInfo) =>
    _events.Add(eventInfo);

  public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) =>
    _ctors.Cast<ConstructorInfo>().ToArray();

  public override EventInfo GetEvent(string name, BindingFlags bindingAttr) =>
    GetEvents().Single(ei => ei.Name == name);

  public override EventInfo[] GetEvents(BindingFlags bindingAttr) =>
    _events.ToArray();

  protected override PropertyInfo GetPropertyImpl(
    string name,
    BindingFlags bindingAttr,
    Binder binder,
    Type returnType,
    Type[] types,
    ParameterModifier[] modifiers)
  {
    return GetProperties().Single(prop => prop.Name == name);
  }

  public override PropertyInfo[] GetProperties(BindingFlags bindingAttr) =>
    _properties.ToArray();

  protected override MethodInfo GetMethodImpl(
    string name,
    BindingFlags bindingAttr,
    Binder binder,
    CallingConventions callConvention,
    Type[] types,
    ParameterModifier[] modifiers)
  {
    var methodGroup = GetMethods().Where(method =>
      method.Name == name);
    if (types == null)
    {
      // Parameters unknown from caller. Hope we have only one method to return.
      return methodGroup.Single();
    }

    bool overloadsComparer(MethodInfo method)
    {
      var parameters = method.GetParameters();
      // Compare Full Names mainly because the RemoteMethodInfo contains
      // RemoteParameterInfos and we might be comparing with local parameters
      // (like System.String)
      bool matchingExpectingTypes = parameters
        .Select(arg => arg.ParameterType.FullName)
        .SequenceEqual(types.Select(type => type.FullName));
      return matchingExpectingTypes;
    }

    // Need to filer also by types
    return methodGroup.Single(overloadsComparer);
  }

  public override MethodInfo[] GetMethods(BindingFlags bindingAttr) =>
    _methods.ToArray();

  public override FieldInfo GetField(string name, BindingFlags bindingAttr) =>
    GetFields().Single(field => field.Name == name);

  public override FieldInfo[] GetFields(BindingFlags bindingAttr) =>
    _fields.ToArray();

  private IEnumerable<MemberInfo> GetMembersInner(BindingFlags flags)
  {
    foreach (var ctor in GetConstructors(flags))
    {
      yield return ctor;
    }
    foreach (var field in GetFields(flags))
    {
      yield return field;
    }
    foreach (var prop in GetProperties(flags))
    {
      yield return prop;
    }
    foreach (var @event in GetEvents(flags))
    {
      yield return @event;
    }
    foreach (var method in GetMethods(flags))
    {
      yield return method;
    }
  }

  internal void SetParent(Lazy<Type> parent) => _parent = parent;

  public override MemberInfo[] GetMembers(BindingFlags bindingAttr) =>
    GetMembersInner(bindingAttr).ToArray();

  protected override bool IsArrayImpl() => _isArray;

  public override string ToString() => FullName;
}
