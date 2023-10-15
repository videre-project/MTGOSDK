/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API;

public class User(dynamic /* IUser */ user)
{
  //
  // UserManager wrapper methods
  //

  /// <summary>
  /// This class manages the client's caching and updating of user information.
  /// </summary>
  private static readonly dynamic s_userManager =
    //
    // We must call the internal GetInstance() method to retrieve PropertyInfo
    // data from the remote type as the local proxy type or ObjectProvider will
    // restrict access to internal or private members.
    //
    // This is a limitation of the current implementation of the Proxy<T> type
    // since any MemberInfo data is cached by the runtime and will conflict
    // with RemoteNET's internal type reflection methods.
    //
    RemoteClient.GetInstance("WotC.MtGO.Client.Model.Core.UserManager");

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  private static User GetUser(int id, string name) =>
    new User(
      s_userManager.CreateNewUser(id, name)
        ?? throw new Exception($"Failed to retrieve user '{name}' (#{id}).")
    );

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  private static User GetUser(string name) =>
    GetUser(
      GetUserId(name)
        ?? throw new Exception($"User '{name}' does not exist."),
      name
    );

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>A new User object.</returns>
  private static User GetUser(int id) =>
    GetUser(
      id,
      GetUserName(id)
        ?? throw new Exception($"User #{id} does not exist.")
    );

  /// <summary>
  /// Retrieves the username of a user by their Login ID.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>The display name of the user.</returns>
  public static string GetUserName(int id) => s_userManager.GetUserName(id);

  /// <summary>
  /// Retrieves the Login ID of a user by their username.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <returns>The Login ID of the user.</returns>
  public static int? GetUserId(string name) => s_userManager.GetUserId(name);

  public User(int id) : this(GetUser(id))
  { }
  public User(string name) : this(GetUser(name))
  { }
  public User(int id, string name) : this(GetUser(id, name))
  { }

  //
  // IUser wrapper properties
  //

  /// <summary>
  /// Internal unwrapped reference to any captured IUser objects.
  /// </summary>
  private dynamic user = user is User ? user.user : user
    ?? throw new Exception($"User object is not a valid IUser type.");

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
