/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API.Users;

public static class UserManager
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
  public static User GetUser(int id, string name) =>
    new User(
      // This is a private method that is not exposed by the IUserManager type.
      s_userManager.CreateNewUser(id, name)
        ?? throw new Exception($"Failed to retrieve user '{name}' (#{id}).")
    );

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  public static User GetUser(string name) =>
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
  public static User GetUser(int id) =>
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
}
