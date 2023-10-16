/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API;

public class User(dynamic /* IUser */ user)
{
  /// <summary>
  /// Internal unwrapped reference to any captured IUser objects.
  /// </summary>
  /// <remarks>
  /// This is used to extract the (dynamic) IUser object from any passed in
  /// User objects, deferring any dynamic dispatching of User constructors.
  /// </remarks>
  private dynamic user = user is User ? user.user : user
    ?? throw new Exception($"User object is not a valid IUser type.");

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
  public int Id => user.Id;

  /// <summary>
  /// The display name of the user.
  /// </summary>
  public string Name => user.Name;

  /// <summary>
  /// The Catalog ID of the user's avatar.
  /// </summary>
  public int AvatarId => user.AvatarID;

  /// <summary>
  /// The user's avatar resource.
  /// </summary>
  public IAvatar Avatar => Proxy<IAvatar>.As(user.CurrentAvatar);

  /// <summary>
  /// Whether the account is not a fully activated account.
  /// </summary>
  public bool IsGuest => user.IsGuest;

  /// <summary>
  /// Whether the user is added as a buddy of the current user.
  /// </summary>
  public bool IsBuddy => user.IsBuddy;

  /// <summary>
  /// Whether the user is blocked by the current user.
  /// </summary>
  public bool IsBlocked => user.IsBlocked;

  /// <summary>
  /// Whether the user is logged in and visible to other users.
  /// </summary>
  public bool IsLoggedIn => user.IsLoggedInAndVisible;

  /// <summary>
  /// The user's last login timestamp.
  /// </summary>
  public string LastLogin => user.LastLogin;
}
