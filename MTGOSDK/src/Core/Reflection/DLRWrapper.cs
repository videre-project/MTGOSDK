/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// A wrapper for dynamic objects that implement an interface at runtime.
/// </summary>
/// <remarks>
/// This class exposes an overrideable <see cref="obj"/> property that is used
/// to capture dynamic objects passed to the constructor. This allows derived
/// classes to defer dynamic dispatching of class constructors until after
/// the base class constructor has completed, exposing the captured dynamic
/// object to derived classes with the <see cref="@base"/> property.
/// </remarks>
/// <typeparam name="T">The interface type to wrap.</typeparam>
public class DLRWrapper<T>() where T : class
{
  /// <summary>
  /// This is the internal reference for any dynamic or derived class objects.
  /// </summary>
  internal virtual dynamic obj =>
    //
    // Derived classes must override this property to capture dynamic objects.
    //
    throw new Exception(
        $"{nameof(DLRWrapper<T>)}.obj must capture a {typeof(T).Name} type.");

  /// <summary>
  /// Internal unwrapped reference to any captured dynamic objects.
  /// </summary>
  /// <remarks>
  /// This is used to extract dynamic objects passed from any derived
  /// classes, deferring any dynamic dispatching of class constructors.
  /// </remarks>
  internal dynamic @base => obj is DLRWrapper<T> ? obj.obj : obj
    ?? throw new Exception(
        $"{nameof(DLRWrapper<T>)} object has no valid {typeof(T).Name} type.");
}
