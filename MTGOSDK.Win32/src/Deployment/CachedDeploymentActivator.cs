/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;


namespace MTGOSDK.Win32.Deployment;

/// <summary>
/// Activates the application already installed in the current user's
/// ClickOnce store without asking the deployment provider for an update.
/// </summary>
/// <remarks>
/// This class targets the .NET Framework ClickOnce runtime used by the
/// standalone MTGO launcher. The deployment assembly is loaded by name so
/// this netstandard2.0 library does not take a framework-specific reference.
/// </remarks>
internal static class CachedDeploymentActivator
{
  private const BindingFlags InstanceFlags =
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
  private const BindingFlags StaticFlags =
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

  /// <summary>
  /// Activates the deployment referenced by an appref-ms file using the
  /// current ClickOnce subscription bind.
  /// </summary>
  /// <param name="appRefPath">The path to the MTGO appref-ms file.</param>
  public static void Activate(string appRefPath)
  {
    string subscriptionId = ReadSubscriptionId(appRefPath);
    Assembly deploymentAssembly = LoadDeploymentAssembly();

    Type storeType = GetRequiredType(
        deploymentAssembly,
        "System.Deployment.Application.SubscriptionStore");
    Type identityType = GetRequiredType(
        deploymentAssembly,
        "System.Deployment.Application.DefinitionIdentity");
    Type managerType = GetRequiredType(
        deploymentAssembly,
        "System.Deployment.Application.DeploymentManager");

    object store = GetRequiredProperty(storeType, "CurrentUser", StaticFlags)
      .GetValue(null)
      ?? throw new InvalidOperationException(
          "The current ClickOnce subscription store is unavailable.");

    object identity = InvokeConstructor(
        GetRequiredConstructor(identityType, typeof(string)),
        new object[] { subscriptionId });
    object state = Invoke(
        GetRequiredMethod(
            storeType,
            "GetSubscriptionState",
            InstanceFlags,
            identityType),
        store,
        new object[] { identity })
      ?? throw new InvalidOperationException(
          "The ClickOnce subscription state could not be read.");
    object currentBind = GetRequiredProperty(
            state.GetType(),
            "CurrentBind",
            InstanceFlags)
        .GetValue(state)
        ?? throw new InvalidOperationException(
            "The ClickOnce subscription has no current application bind.");

    object manager = InvokeConstructor(
        GetRequiredConstructor(managerType, typeof(string)),
        new object[] { currentBind.ToString()! });

    using ((IDisposable)manager)
    {
      Invoke(
          GetRequiredMethod(managerType, "Bind", InstanceFlags),
          manager,
          null);
      Invoke(
          GetRequiredMethod(managerType, "ExecuteNewProcess", InstanceFlags),
          manager,
          null);
    }
  }

  private static Assembly LoadDeploymentAssembly()
  {
    const string resourceName = "System.Deployment.dll";
    using Stream? stream = typeof(CachedDeploymentActivator).Assembly
      .GetManifestResourceStream(resourceName);
    if (stream is not null)
    {
      using var buffer = new MemoryStream();
      stream.CopyTo(buffer);
      return Assembly.Load(buffer.ToArray());
    }

    return Assembly.Load("System.Deployment");
  }

  private static string ReadSubscriptionId(string appRefPath)
  {
    if (!File.Exists(appRefPath))
      throw new FileNotFoundException(
          "The ClickOnce appref-ms file was not found.",
          appRefPath);

    string content = File.ReadAllText(appRefPath, Encoding.Unicode);
    int fragmentStart = content.IndexOf('#');
    if (fragmentStart < 0)
      throw new InvalidOperationException(
          "The ClickOnce appref-ms file has no subscription identity.");

    string subscriptionId = Uri.UnescapeDataString(
        content.Substring(fragmentStart + 1).Trim('\0', '\r', '\n', ' '));
    if (string.IsNullOrWhiteSpace(subscriptionId))
      throw new InvalidOperationException(
          "The ClickOnce appref-ms file has an empty subscription identity.");

    return subscriptionId;
  }

  private static Type GetRequiredType(Assembly assembly, string typeName) =>
    assembly.GetType(typeName, throwOnError: true)!;

  private static PropertyInfo GetRequiredProperty(
    Type type,
    string propertyName,
    BindingFlags flags) =>
    type.GetProperty(propertyName, flags)
      ?? throw new MissingMemberException(type.FullName, propertyName);

  private static MethodInfo GetRequiredMethod(
    Type type,
    string methodName,
    BindingFlags flags,
    Type? parameterType = null)
  {
    foreach (MethodInfo method in type.GetMethods(flags))
    {
      if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
        continue;

      ParameterInfo[] parameters = method.GetParameters();
      if (parameterType is null
          ? parameters.Length == 0
          : parameters.Length == 1
            && parameters[0].ParameterType == parameterType)
      {
        return method;
      }
    }

    throw new MissingMethodException(type.FullName, methodName);
  }

  private static ConstructorInfo GetRequiredConstructor(Type type, Type parameterType) =>
    type.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: new[] { parameterType },
        modifiers: null)
      ?? throw new MissingMethodException(type.FullName, ".ctor");

  private static object InvokeConstructor(
    ConstructorInfo constructor,
    object[] arguments)
  {
    try
    {
      return constructor.Invoke(arguments);
    }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
      ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
      throw;
    }
  }

  private static object? Invoke(
    MethodInfo method,
    object target,
    object?[]? arguments)
  {
    try
    {
      return method.Invoke(target, arguments);
    }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
      ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
      throw;
    }
  }
}
