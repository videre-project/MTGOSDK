/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;
using System.Reflection;


namespace MTGOSDK.Core.Reflection.Types;

public class TypeStub(
    string name = nameof(TypeStub),
    Guid guid = default,
    Module module = default,
    Assembly assembly = default,
    string fullName = default,
    string @namespace = default,
    string assemblyQualifiedName = default,
    Type baseType = default,
    Type underlyingSystemType = default) : Type
{
  public override string Name => name;
  public override Guid GUID => guid;
  public override Module Module => module;
  public override Assembly Assembly => assembly;
  public override string FullName => fullName;
  public override string Namespace => @namespace;
  public override string AssemblyQualifiedName => assemblyQualifiedName;
  public override Type BaseType => baseType;
  public override Type UnderlyingSystemType => underlyingSystemType;

  public override object[] GetCustomAttributes(bool inherit)
  {
    throw new NotImplementedException();
  }

  public override bool IsDefined(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }

  public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override Type GetInterface(string name, bool ignoreCase)
  {
    throw new NotImplementedException();
  }

  public override Type[] GetInterfaces()
  {
    throw new NotImplementedException();
  }

  public override EventInfo GetEvent(string name, BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override EventInfo[] GetEvents(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override Type[] GetNestedTypes(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override Type GetNestedType(string name, BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override Type GetElementType()
  {
    throw new NotImplementedException();
  }

  protected override bool HasElementTypeImpl()
  {
    throw new NotImplementedException();
  }

  protected override PropertyInfo GetPropertyImpl(
    string name,
    BindingFlags bindingAttr,
    Binder binder,
    Type returnType,
    Type[] types,
    ParameterModifier[] modifiers)
  {
    throw new NotImplementedException();
  }

  public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  protected override MethodInfo GetMethodImpl(
    string name,
    BindingFlags bindingAttr,
    Binder binder,
    CallingConventions callConvention,
    Type[] types,
    ParameterModifier[] modifiers)
  {
    throw new NotImplementedException();
  }

  public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override FieldInfo GetField(string name, BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override FieldInfo[] GetFields(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  public override MemberInfo[] GetMembers(BindingFlags bindingAttr)
  {
    throw new NotImplementedException();
  }

  protected override TypeAttributes GetAttributeFlagsImpl()
  {
    throw new NotImplementedException();
  }

  protected override bool IsArrayImpl()
  {
    throw new NotImplementedException();
  }

  protected override bool IsByRefImpl()
  {
    throw new NotImplementedException();
  }

  protected override bool IsPointerImpl()
  {
    throw new NotImplementedException();
  }

  protected override bool IsPrimitiveImpl()
  {
    throw new NotImplementedException();
  }

  protected override bool IsCOMObjectImpl()
  {
    throw new NotImplementedException();
  }

  public override object InvokeMember(
    string name,
    BindingFlags invokeAttr,
    Binder binder,
    object target,
    object[] args,
    ParameterModifier[] modifiers,
    CultureInfo culture,
    string[] namedParameters)
  {
    throw new NotImplementedException();
  }

  protected override ConstructorInfo GetConstructorImpl(
    BindingFlags bindingAttr,
    Binder binder,
    CallingConventions callConvention,
    Type[] types,
    ParameterModifier[] modifiers)
  {
    throw new NotImplementedException();
  }

  public override object[] GetCustomAttributes(Type attributeType, bool inherit)
  {
    throw new NotImplementedException();
  }
}
