/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Users;

public sealed class User(dynamic user) : DLRWrapper<IUser>
{
  /// <summary>
  /// Stores an internal reference to the IUser object.
  /// </summary>
  internal override dynamic obj => user; // Input obj is not type-casted.

  public User(int id) : this(UserManager.GetUser(id))
  { }
  public User(string name) : this(UserManager.GetUser(name))
  { }
  public User(int id, string name) : this(UserManager.GetUser(id, name))
  { }

  //
  // IUser wrapper properties
  //

  /// <summary>
  /// The Login ID of the user.
  /// </summary>
  [Default(-1)]
  public int Id => @base.Id;

  /// <summary>
  /// The display name of the user.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The Catalog ID of the user's avatar.
  /// </summary>
  [Default(-1)]
  public int AvatarId => @base.AvatarID;

  /// <summary>
  /// The user's avatar resource.
  /// </summary>
  public Avatar Avatar => new(@base.CurrentAvatar);

  /// <summary>
  /// Whether the account is not a fully activated account.
  /// </summary>
  public bool IsGuest => @base.IsGuest;

  /// <summary>
  /// Whether the user is added as a buddy of the current user.
  /// </summary>
  public bool IsBuddy => @base.IsBuddy;

  /// <summary>
  /// Whether the user is blocked by the current user.
  /// </summary>
  public bool IsBlocked => @base.IsBlocked;

  /// <summary>
  /// Whether the user is logged in and visible to other users.
  /// </summary>
  public bool IsLoggedIn => @base.IsLoggedInAndVisible;

  /// <summary>
  /// The user's last login timestamp.
  /// </summary>
  public string LastLogin => @base.LastLogin;

  //
  // IUser wrapper properties
  //

  public override string ToString() => this.Name;

  //
  // IUser wrapper events
  //

  public EventProxy IsLoggedInAndVisibleChanged =
    new(/* IUser */ user, nameof(IsLoggedInAndVisibleChanged));

  public EventProxy CurrentAvatarChanged =
    new(/* IUser */ user, nameof(CurrentAvatarChanged));

  public EventProxy UserActivated =
    new(/* IUser */ user, nameof(UserActivated));
}
