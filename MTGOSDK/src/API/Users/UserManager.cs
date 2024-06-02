/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Reflection;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Chat;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API.Users;
using static MTGOSDK.API.Events;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

public static class UserManager
{
  /// <summary>
  /// A dictionary of cached user objects by their Login ID.
  /// </summary>
  public static ConcurrentDictionary<int, User> Users { get; } = new();

  //
  // UserManager wrapper methods
  //

  /// <summary>
  /// This class manages the client's caching and updating of user information.
  /// </summary>
  private static readonly IUserManager s_userManager =
    ObjectProvider.Get<IUserManager>();

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the user does not exist.
  /// </exception>
  public static User GetUser(int id, string name)
  {
    if (!Users.TryGetValue(id, out var user))
    {
      Log.Trace("Creating new User object for {Name} (#{Id})", name, id);
      Users[id] = user = new User(
        // This is a private method that is not exposed by the IUserManager type.
        Unbind(s_userManager).CreateNewUser(id, name)
          ?? throw new ArgumentException($"User '{name}' (#{id}) does not exist.")
      );
      // Set callback to remove user from cache when the client is disposed.
      RemoteClient.Disposed += (s, e) => Users.TryRemove(id, out _);
    }

    return user;
  }

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the user does not exist.
  /// </exception>
  public static User GetUser(string name) =>
    GetUser(
      GetUserId(name)
        ?? throw new ArgumentException($"User '{name}' does not exist."),
      name
    );

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the user does not exist.
  /// </exception>
  public static User GetUser(int id) =>
    Users.TryGetValue(id, out var user)
      ? user
      : GetUser(
          id,
          GetUserName(id)
            ?? throw new ArgumentException($"User #{id} does not exist.")
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

  //
  // IBuddyUsersList wrapper methods
  //

  /// <summary>
  /// Internal reference to the client's BuddyUsersList.
  /// </summary>
  private static readonly IBuddyUsersList s_buddyUsersList =
    ObjectProvider.Get<IBuddyUsersList>();

  /// <summary>
  /// Retrieves a list of the current user's buddy users.
  /// </summary>
  public static IEnumerable<User> GetBuddyUsers() =>
    Map<User>(/* IEnumerable<IUser */ s_buddyUsersList);

  //
  // IBuddyUsersList wrapper events
  //

  /// <summary>
  /// Occurs when a buddy user logs in.
  /// </summary>
  public static EventProxy<UserEventArgs> BuddyLoggedIn =
    new(/* IBuddyUsersList */ s_buddyUsersList, nameof(BuddyLoggedIn));
}
