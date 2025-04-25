/** @file
  Copyright (c) 2010, Ekon Benefits.
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using ImpromptuInterface;
using ImpromptuInterface.Build;


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

public static class DynamicTypeBuilder
{
  internal static readonly AssemblyName s_assemblyName =
    new($"{nameof(MTGOSDK)}.RuntimeInternal");

  internal static readonly ModuleBuilder s_builder =
#if NETFRAMEWORK
    AppDomain.CurrentDomain
#else
    AssemblyBuilder
#endif
      .DefineDynamicAssembly(s_assemblyName, AssemblyBuilderAccess.Run)
      .DefineDynamicModule("DynamicModule");

  internal static readonly TypeAssembler s_assembler = new();

  private static readonly ReaderWriterLockSlim _typeCacheLock = new();
  private static readonly Dictionary<TypeHash, Type> _typeHash = new();

  private static string GenerateHash(int length) =>
    Guid.NewGuid().ToString("N").Substring(0, length);

  internal static string GenerateTypeProxyName(string typeName) =>
    $"{nameof(MTGOSDK)}.RuntimeInternal.Proxy.{typeName}_{GenerateHash(8):N}";

  internal static Type GenerateFullDelegate(
    TypeBuilder builder,
    Type returnType,
    IEnumerable<Type> types,
    MethodInfo info = null)
  {
    var tBuilder = s_builder.DefineType(
      GenerateTypeProxyName("Delegate"),
      TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.Public,
      typeof(MulticastDelegate));

    var tReplacedTypes = s_assembler.GetParamTypes(tBuilder, info);

    var tParamTypes = info == null
      ? types.ToList()
      : info.GetParameters().Select(it => it.ParameterType).ToList();

    if (tReplacedTypes != null)
    {
      tParamTypes = tReplacedTypes.Item2.ToList();
    }

    if (info != null)
    {
      tParamTypes.Insert(0, typeof(object));
      tParamTypes.Insert(0, typeof(CallSite));
    }

    var tCon = tBuilder.DefineConstructor(
      MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig |
      MethodAttributes.RTSpecialName, CallingConventions.Standard,
      new[] { typeof(object), typeof(IntPtr) });

    tCon.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

    var tMethod = tBuilder.DefineMethod("Invoke",
      MethodAttributes.Public | MethodAttributes.HideBySig |
      MethodAttributes.NewSlot |
      MethodAttributes.Virtual);

    tMethod.SetReturnType(returnType);
    tMethod.SetParameters(tParamTypes.ToArray());

    if (info != null)
    {
      foreach (var tParam in info.GetParameters())
      {
        //+3 because of the callsite and target are added
        tMethod.DefineParameter(tParam.Position + 3, s_assembler.AttributesForParam(tParam), tParam.Name);
      }
    }

    tMethod.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

    return tBuilder.CreateTypeInfo();

  }

  public static Type BuildTypeHelper(
    ModuleBuilder builder,
    Type contextType,
    IDictionary<string, Type> informalInterface)
  {
    var tB = builder.DefineType(
      GenerateTypeProxyName("InformalInterface"),
      TypeAttributes.Public | TypeAttributes.Class,
      typeof(Proxy));

    foreach (var tInterface in informalInterface)
    {
      s_assembler.MakePropertyDescribedProperty(
        builder,
        tB,
        contextType,
        tInterface.Key,
        tInterface.Value
      );
    }
    var tType = tB.CreateTypeInfo();
    return tType;
  }

  public static Type BuildTypeHelper(
    ModuleBuilder builder,
    Type contextType,
    params Type[] interfaces)
  {
    var tInterfacesMainList = interfaces.Distinct().ToArray();
    var tB = builder.DefineType(
        GenerateTypeProxyName(tInterfacesMainList.First().Name),
        TypeAttributes.Public | TypeAttributes.Class,
        typeof(Proxy), tInterfacesMainList);

    tB.SetCustomAttribute(
        new CustomAttributeBuilder(typeof(ActLikeProxyAttribute)
        .GetConstructor(new[] { typeof(Type).MakeArrayType(), typeof(Type) }),
            new object[] { interfaces, contextType }));
    tB.SetCustomAttribute(new CustomAttributeBuilder(typeof(SerializableAttribute)
        .GetConstructor(Type.EmptyTypes), new object[] { }));

    var tInterfaces = tInterfacesMainList.Concat(
        tInterfacesMainList.SelectMany(it => it.GetInterfaces()));

    var tPropertyNameHash = new HashSet<string>();
    var tMethodHashSet = new HashSet<MethodSigHash>();

    object tAttr = null;
    foreach (var tInterface in tInterfaces.Distinct())
    {
      if (tInterface != null && tAttr == null)
      {
        var tCustomAttributes = tInterface.GetCustomAttributesData();
        foreach (var tCustomAttribute in tCustomAttributes.Where(it => typeof(DefaultMemberAttribute).IsAssignableFrom(it.Constructor.DeclaringType)))
        {
          try
          {
            tB.SetCustomAttribute(s_assembler.GetAttributeBuilder(tCustomAttribute));
          }
          catch
          {
            // For most proxies not having the same attributes won't really
            // matter, but just incase we don't want to stop for some unknown
            // attribute that we can't initialize.
          }
        }
      }

      var tNonRecursive = tInterface.GetCustomAttributes(typeof(NonRecursiveInterfaceAttribute), true).Any();

      foreach (var tInfo in tInterface.GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        var tNonRecursiveProp = tNonRecursive ||
            tInfo.GetCustomAttributes(typeof(NonRecursiveInterfaceAttribute), true).Any();

        s_assembler.MakeProperty(builder, tInfo, tB, contextType, nonRecursive: tNonRecursiveProp, defaultImp: tPropertyNameHash.Add(tInfo.Name));
      }
      foreach (var tInfo in tInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(it => !it.IsSpecialName))
      {
        var tNonRecursiveMeth = tNonRecursive ||
            tInfo.GetCustomAttributes(typeof(NonRecursiveInterfaceAttribute), true).Any();
        s_assembler.MakeMethod(builder, tInfo, tB, contextType, nonRecursive: tNonRecursiveMeth, defaultImp: tMethodHashSet.Add(new MethodSigHash(tInfo)));
      }
      foreach (var tInfo in tInterface.GetEvents(BindingFlags.Public | BindingFlags.Instance).Where(it => !it.IsSpecialName))
      {
        s_assembler.MakeEvent(builder, tInfo, tB, contextType, defaultImp: tPropertyNameHash.Add(tInfo.Name));
      }
    }
    var tType = tB.CreateTypeInfo();
    return tType;
  }

  /// <summary>
  /// Builds the type for the proxy or returns from cache
  /// </summary>
  /// <param name="contextType">Type of the context.</param>
  /// <param name="mainInterface">The main interface.</param>
  /// <param name="otherInterfaces">The other interfaces.</param>
  /// <returns></returns>
  public static Type BuildType(
    Type contextType,
    Type mainInterface,
    params Type[] otherInterfaces)
  {
    _typeCacheLock.EnterUpgradeableReadLock();
    try
    {
      // Check if the type is already in the cache
      var tNewHash = TypeHash.Create(contextType,
          new[] { mainInterface }.Concat(otherInterfaces).ToArray());
      if (_typeHash.TryGetValue(tNewHash, out var tType))
        return tType;

      // Upgrade to write lock
      _typeCacheLock.EnterWriteLock();
      try
      {
        // Check again in case another thread added it while we were waiting
        if (_typeHash.TryGetValue(tNewHash, out tType))
          return tType;

        // Create the new type and add it to the cache
        tType = BuildTypeHelper(s_builder, contextType,
            new[] { mainInterface }.Concat(otherInterfaces).ToArray());
        _typeHash[tNewHash] = tType;

        return tType;
      }
      finally
      {
        _typeCacheLock.ExitWriteLock();
      }
    }
    finally
    {
      _typeCacheLock.ExitUpgradeableReadLock();
    }
  }

  /// <summary>
  /// Builds the type.
  /// </summary>
  /// <param name="contextType">Type of the context.</param>
  /// <param name="informalInterface">The informal interface.</param>
  /// <returns></returns>
  public static Type BuildType(
    Type contextType,
    IDictionary<string, Type> informalInterface)
  {
    // Enter upgradeable read lock
    _typeCacheLock.EnterUpgradeableReadLock();
    try
    {
      // Check if the type is already in the cache
      var tNewHash = TypeHash.Create(contextType, informalInterface);
      if (_typeHash.TryGetValue(tNewHash, out var tType))
        return tType;

      // Upgrade to write lock
      _typeCacheLock.EnterWriteLock();
      try
      {
        // Check again in case another thread added it while we were waiting
        if (_typeHash.TryGetValue(tNewHash, out tType))
          return tType;

        // Create the new type and add it to the cache
        tType = BuildTypeHelper(s_builder, contextType, informalInterface);
        _typeHash[tNewHash] = tType;

        return tType;
      }
      finally
      {
        _typeCacheLock.ExitWriteLock();
      }
    }
    finally
    {
      _typeCacheLock.ExitUpgradeableReadLock();
    }
  }
}
