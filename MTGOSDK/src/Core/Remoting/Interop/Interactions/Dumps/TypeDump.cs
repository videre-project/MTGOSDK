/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Diagnostics;
using System.Reflection;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[DebuggerDisplay("TypeDump of {" + nameof(Type) + "} (Assembly: {" + nameof(Assembly) + "})")]
public class TypeDump
{
  public struct TypeMethod
  {
    public struct MethodParameter
    {
      public bool IsGenericType { get; set; }
      public bool IsGenericParameter { get; set; }
      public string Type { get; set; }
      public string Name { get; set; }
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

    public string Visibility { get; set; }
    public string Name { get; set; }
    public string ReturnTypeFullName { get; set; }
    // This is not a list of the PARAMETERS which are generic -> This is the list of TYPES place holders usually found between
    // the "LESS THEN" and "GEATER THEN" signs so for this methods:
    // void SomeMethod<T,S>(T item, string item2, S item3)
    // You'll get ["T", "S"]
    public List<string> GenericArgs { get; set; }
    public List<MethodParameter> Parameters { get; set; }
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

  public struct TypeField(FieldInfo fi)
  {
    public string Visibility = fi.IsPublic ? "Public" : "Private";
    public string Name = fi.Name;
    public string TypeFullName = fi.FieldType.FullName;
    public string Assembly = fi.FieldType.Assembly.GetName().Name;
  }

  public struct TypeEvent(EventInfo ei)
  {
    public string Name = ei.Name;
    public string TypeFullName = ei.EventHandlerType.FullName;
    public string Assembly = ei.EventHandlerType.Assembly.GetName().Name;
  }

  public struct TypeProperty(PropertyInfo pi)
  {
    public string Name = pi.Name;
    public string TypeFullName = pi.PropertyType.FullName;
    public string Assembly = pi.PropertyType.Assembly.GetName().Name;
    public string GetVisibility = pi.GetMethod != null
      ? (pi.GetMethod.IsPublic ? "Public" : "Private")
      : null!;
    public string SetVisibility = pi.SetMethod != null
      ? (pi.SetMethod.IsPublic ? "Public" : "Private")
      : null!;
  }

  public string Type { get; set; }
  public string Assembly { get; set; }

  public bool IsArray { get; set; }

  public string ParentFullTypeName { get; set; }
  public string ParentAssembly { get; set; }

  public List<TypeMethod> Methods { get; set; }
  public List<TypeMethod> Constructors { get; set; }
  public List<TypeField> Fields { get; set; }
  public List<TypeEvent> Events { get; set; }
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
