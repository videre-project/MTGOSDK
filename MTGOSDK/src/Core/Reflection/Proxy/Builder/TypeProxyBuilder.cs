/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Linq.Expressions;

using ImpromptuInterface.Optimization;


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

public static class TypeProxyBuilder
{
  // Cache for compiled proxy factory delegates: Type -> Func<IProxyInitialize>
  private static readonly ConcurrentDictionary<Type, Func<IProxyInitialize>> s_proxyFactoryCache = new();
  /// <summary>
  /// Fixes the target context of the specified object.
  /// </summary>
  /// <param name="obj">The object to fix.</param>
  /// <param name="tContext">The context type.</param>
  /// <returns>The fixed object.</returns>
  public static dynamic FixTargetContext(object obj, out Type? tContext)
  {
    if (obj == null)
      throw new ArgumentNullException(nameof(obj), "Object cannot be null.");

    obj = obj.GetTargetContext(out tContext, out var _);
    tContext = tContext.FixContext();

    return obj;
  }

  /// <summary>
  /// Validates the naming convention of the runtime type.
  /// </summary>
  /// <param name="obj">The object to validate.</param>
  /// <returns>True if the runtime type is valid, false otherwise.</returns>
  public static bool IsValidRuntimeType(object obj) =>
    obj.GetType().FullName
      .StartsWith(DynamicTypeBuilder.s_assemblyName.FullName);

  internal static object InitializeProxy(
    Type proxytype,
    object original,
    IEnumerable<Type> interfaces = null,
    IDictionary<string, Type> propertySpec = null,
    TypeAssembler maker = null)
  {
    // Use cached factory delegate instead of slow Activator.CreateInstance
    var factory = s_proxyFactoryCache.GetOrAdd(proxytype, type =>
    {
      // Compile a parameterless constructor call: () => new ProxyType()
      var newExpr = Expression.New(type);
      var lambda = Expression.Lambda<Func<IProxyInitialize>>(newExpr);
      return lambda.Compile();
    });

    var tProxy = factory();
    tProxy.Initialize(original, interfaces, propertySpec,
                      maker ?? DynamicTypeBuilder.s_assembler);
    return tProxy;
  }

  /// <summary>
  /// Private helper method that initializes the proxy.
  /// </summary>
  /// <param name="proxytype">The proxytype.</param>
  /// <param name="original">The original.</param>
  /// <param name="interfaces">The interfaces.</param>
  /// <param name="propertySpec">The property spec.</param>
  /// <returns></returns>
  public static TInterface InitializeProxy<TInterface>(
    Type proxytype,
    object original,
    IEnumerable<Type> interfaces,
    IDictionary<string, Type> propertySpec = null) =>
      (TInterface)InitializeProxy(proxytype, original, interfaces, propertySpec);

  /// <summary>
  /// Private helper method that initializes the proxy.
  /// </summary>
  /// <param name="proxytype">The proxytype.</param>
  /// <param name="original">The original.</param>
  /// <param name="propertySpec">The property spec.</param>
  /// <returns></returns>
  public static TInterface InitializeProxy<TInterface>(
    Type proxytype,
    object original,
    IDictionary<string, Type> propertySpec = null) =>
      InitializeProxy<TInterface>(proxytype, original,
          new[] { typeof (TInterface) }, propertySpec);
}
