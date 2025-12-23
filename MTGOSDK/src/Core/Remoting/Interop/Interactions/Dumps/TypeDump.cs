/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Reflection;

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[DebuggerDisplay("TypeDump of {" + nameof(Type) + "} (Assembly: {" + nameof(Assembly) + "})")]
[MessagePackObject]
public class TypeDump
{
  [MessagePackObject]
  public struct TypeMethod
  {
    [MessagePackObject]
    public struct MethodParameter
    {
      [Key(0)]
      public bool IsGenericType { get; set; }
      [Key(1)]
      public bool IsGenericParameter { get; set; }
      [Key(2)]
      public string Type { get; set; }
      [Key(3)]
      public string Name { get; set; }
      [Key(4)]
      public string Assembly { get; set; }

      public MethodParameter() {}

      public MethodParameter(ParameterInfo pi)
      {
        IsGenericType = pi.ParameterType.IsGenericType;
        IsGenericParameter = pi.ParameterType.IsGenericParameter || pi.ParameterType.ContainsGenericParameters;
        Name = pi.Name;
        // For generic type parameters we need the 'Name' property - it returns something like "T"
        // For non-generic we want the full name like "System.Text.StringBuilder"
        Type = IsGenericParameter ? pi.ParameterType.Name : pi.ParameterType.FullName;
        if (IsGenericParameter &&
          pi.ParameterType.GenericTypeArguments.Any() &&
          Type.Contains('`'))
        {
          Type = Type.Substring(0, Type.IndexOf('`'));
          Type += '<';
          Type += String.Join(", ", (object[])pi.ParameterType.GenericTypeArguments);
          Type += '>';
        }
        Assembly = pi.ParameterType.Assembly.GetName().Name;
      }

      public override string ToString()
      {
        return
          (string.IsNullOrEmpty(Assembly) ? string.Empty : Assembly + ".") +
          (string.IsNullOrEmpty(Type) ? "UNKNOWN_TYPE" : Type) + " " +
              (string.IsNullOrEmpty(Name) ? "MISSING_NAME" : Name);
      }
    }

    [Key(0)]
    public string Visibility { get; set; }
    [Key(1)]
    public string Name { get; set; }
    [Key(2)]
    public string ReturnTypeFullName { get; set; }
    // This is not a list of the PARAMETERS which are generic -> This is the list of TYPES place holders usually found between
    // the "LESS THEN" and "GEATER THEN" signs so for this methods:
    // void SomeMethod<T,S>(T item, string item2, S item3)
    // You'll get ["T", "S"]
    [Key(3)]
    public List<string> GenericArgs { get; set; }
    [Key(4)]
    public List<MethodParameter> Parameters { get; set; }
    [Key(5)]
    public string ReturnTypeAssembly { get; set; }

    public TypeMethod() {}

    public TypeMethod(MethodBase methodBase)
    {
      Visibility = methodBase.IsPublic ? "Public" : "Private";
      GenericArgs = new List<string>();
      if (methodBase.ContainsGenericParameters && methodBase is not ConstructorInfo)
      {
        try
        {
          GenericArgs = methodBase.GetGenericArguments().Select(arg => arg.Name).ToList();
        }
        catch (Exception) {}
      }

      Name = methodBase.Name;
      Parameters = methodBase.GetParameters().Select(paramInfo => new MethodParameter(paramInfo)).ToList();
      if (methodBase is MethodInfo methodInfo)
      {
        ReturnTypeFullName = methodInfo.ReturnType.FullName;
        if (ReturnTypeFullName == null)
        {
          string baseType = methodInfo.ReturnType.Name;
          if (baseType.Contains('`'))
            baseType = baseType.Substring(0, baseType.IndexOf('`'));
          ReturnTypeFullName ??= baseType + "<" +
                        String.Join(", ", (object[])methodInfo.ReturnType.GenericTypeArguments) +
                        ">";
        }

        ReturnTypeAssembly = methodInfo.ReturnType.Assembly.GetName().Name;
      }
      else
      {
        ReturnTypeFullName = "System.Void";
        ReturnTypeAssembly = "mscorlib";
      }
    }

    public bool SignaturesEqual(TypeMethod other)
    {
      if (Name != other.Name)
        return false;
      if (Parameters.Count != other.Parameters.Count)
        return false;
      var genericArgsMatches = GenericArgs.Zip(other.GenericArgs, (arg1, arg2) =>
      {
        return arg1 == arg2;
      });
      var paramMatches = Parameters.Zip(other.Parameters, (param1, param2) =>
      {
        return param1.Name == param2.Name &&
              param1.Type == param2.Type;
      });
      return paramMatches.All(match => match == true);
    }

    public override string ToString()
    {
      return $"{ReturnTypeFullName} {Name}({string.Join(",", Parameters)})";
    }
  }

  [MessagePackObject]
  public struct TypeField
  {
    [Key(0)]
    public string Visibility { get; set; }
    [Key(1)]
    public string Name { get; set; }
    [Key(2)]
    public string TypeFullName { get; set; }
    [Key(3)]
    public string Assembly { get; set; }

    public TypeField() {}

    public TypeField(FieldInfo fi)
    {
      Visibility = fi.IsPublic ? "Public" : "Private";
      Name = fi.Name;
      TypeFullName = fi.FieldType.FullName;
      Assembly = fi.FieldType.Assembly.GetName().Name;
    }
  }

  [MessagePackObject]
  public struct TypeEvent
  {
    [Key(0)]
    public string Name { get; set; }
    [Key(1)]
    public string TypeFullName { get; set; }
    [Key(2)]
    public string Assembly { get; set; }

    public TypeEvent() {}

    public TypeEvent(EventInfo ei)
    {
      Name = ei.Name;
      TypeFullName = ei.EventHandlerType.FullName;
      Assembly = ei.EventHandlerType.Assembly.GetName().Name;
    }
  }

  [MessagePackObject]
  public struct TypeProperty
  {
    [Key(0)]
    public string Name { get; set; }
    [Key(1)]
    public string TypeFullName { get; set; }
    [Key(2)]
    public string Assembly { get; set; }
    [Key(3)]
    public string GetVisibility { get; set; }
    [Key(4)]
    public string SetVisibility { get; set; }

    public TypeProperty() {}

    public TypeProperty(PropertyInfo pi)
    {
      Name = pi.Name;
      TypeFullName = pi.PropertyType.FullName;
      Assembly = pi.PropertyType.Assembly.GetName().Name;
      GetVisibility = pi.GetMethod != null
        ? (pi.GetMethod.IsPublic ? "Public" : "Private")
        : null!;
      SetVisibility = pi.SetMethod != null
        ? (pi.SetMethod.IsPublic ? "Public" : "Private")
        : null!;
    }
  }

  [Key(0)]
  public string Type { get; set; }
  [Key(1)]
  public string Assembly { get; set; }

  [Key(2)]
  public bool IsArray { get; set; }

  [Key(3)]
  public string ParentFullTypeName { get; set; }
  [Key(4)]
  public string ParentAssembly { get; set; }

  [Key(5)]
  public List<TypeMethod> Methods { get; set; }
  [Key(6)]
  public List<TypeMethod> Constructors { get; set; }
  [Key(7)]
  public List<TypeField> Fields { get; set; }
  [Key(8)]
  public List<TypeEvent> Events { get; set; }
  [Key(9)]
  public List<TypeProperty> Properties { get; set; }

  public static TypeDump ParseType(Type typeObj)
    {
      if (typeObj == null) return null;

      var ctors = typeObj.GetConstructors((BindingFlags)0xffff).Select(ci => new TypeDump.TypeMethod(ci))
        .ToList();
      var methods = typeObj.GetRuntimeMethods().Select(mi => new TypeDump.TypeMethod(mi))
        .ToList();
      var fields = typeObj.GetRuntimeFields().Select(fi => new TypeDump.TypeField(fi))
        .ToList();
      var events = typeObj.GetRuntimeEvents().Select(ei => new TypeDump.TypeEvent(ei))
        .ToList();
      var props = typeObj.GetRuntimeProperties().Select(pi => new TypeDump.TypeProperty(pi))
        .ToList();

      TypeDump td = new()
      {
        Type = typeObj.FullName,
        Assembly = typeObj.Assembly.GetName().Name,
        Methods = methods,
        Constructors = ctors,
        Fields = fields,
        Events = events,
        Properties = props,
        IsArray = typeObj.IsArray,
      };
      if (typeObj.BaseType != null)
      {
        // Has parent. Add its identifier
        td.ParentFullTypeName = typeObj.BaseType.FullName;
        td.ParentAssembly = typeObj.BaseType.Assembly.GetName().Name;
      }

      return td;
    }
}
