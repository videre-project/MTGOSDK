/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Settings;
using static MTGOSDK.API.Events;

/// <summary>
/// A wrapper for the MTGO client's <see cref="IPrimativeSetting"/> interface.
/// </summary>
public sealed class PrimitiveSetting<T>(dynamic setting)
    : DLRWrapper<IPrimitiveSetting<T>>, ISetting
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IPrimitiveSetting<T>);

  /// <summary>
  /// Stores an internal reference to the IChatChannel object.
  /// </summary>
  internal override dynamic obj => Bind<IPrimitiveSetting<T>>(setting);

  public static implicit operator PrimitiveSetting<T>(PrimitiveSetting<object> v) =>
    new(v.obj);

  //
  // IPrimitiveSetting wrapper properties
  //

  /// <summary>
  /// The setting's value.
  /// </summary>
  public T Value => Cast<T>(Unbind(@base).Value);

  //
  // ISetting wrapper properties
  //

  /// <summary>
  /// The unique identifier for the setting (in this case, the setting's key).
  /// </summary>
  public Setting Id => Cast<Setting>(Unbind(@base).ID);

  /// <summary>
  /// Indicates whether the setting has been loaded or is uninitialized.
  /// </summary>
  public bool IsLoaded => @base.IsLoaded;

  /// <summary>
  /// Indicates whether the setting is set to its default value.
  /// </summary>
  public bool IsDefault => @base.IsDefault;

  /// <summary>
  /// Indicates whether the setting can be changed on the client.
  /// </summary>
  public bool IsReadOnly => @base.IsReadOnly;

  /// <summary>
  /// Indicates whether the setting is stored to a local file.
  /// </summary>
  public bool StoreLocally => @base.StoreLocally;

  //
  // ISetting wrapper events
  //

  public EventProxy<SettingEventArgs> IsLoadedChanged =
    new(/* ISetting */ setting, nameof(IsLoadedChanged));

  public EventProxy<SettingEventArgs> IsDefaultChanged =
    new(/* ISetting */ setting, nameof(IsDefaultChanged));

  public EventProxy<SettingEventArgs> ValueChanged =
    new(/* ISetting */ setting, nameof(ValueChanged));
}
