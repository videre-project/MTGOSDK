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
using static MTGOSDK.Core.Reflection.DLRWrapper;

public static class UserManager
{
  /// <summary>
  /// A dictionary of cached user objects by their Login ID.
  /// </summary>
  public static ConcurrentDictionary<int, User> Users { get; } = new();

  /// <summary>
  /// A map of user objects from their display name to their Login ID.
  /// </summary>
  public static ConcurrentDictionary<string, int> UserIds { get; } = new();

  /// <summary>
  /// Resets the local user cache.
  /// </summary>
  public static void ClearCache()
  {
    Users.Clear();
    UserIds.Clear();
  }

  //
  // UserManager wrapper methods
  //

  /// <summary>
  /// Manager for the client's caching and updating of user information.
  /// </summary>
  private static readonly IUserManager s_userManager =
    ObjectProvider.Get<IUserManager>();

  private static readonly dynamic UsersById =
    Unbind(s_userManager).m_usersById;

  /// <summary>
  /// Retrieves a user object by id from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>A new User object.</returns>
  private static User? GetUserById(int id)
  {
    if (!UsersById.ContainsKey(id)) return null;

    Log.Trace("Retrieving user #{Id} from UserManager...", id);
    return new User(UsersById[id]);
  }

  /// <summary>
  /// Creates a new user object in the client's UserManager.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>A new User object.</returns>
  private static User CreateNewUser(string name, int id)
  {
    Log.Trace("Creating new User object for {Name} (#{Id})", name, id);
    return new User(Unbind(s_userManager).CreateNewUser(id, name));
  }

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the user does not exist or does not match the given name.
  /// </exception>
  public static User GetUser(int id, string name)
  {
    // Guard against invalid inputs.
    if (id <= 0)
    {
      throw new ArgumentException(
          $"User ID must be greater than zero. Got {id}.");
    }
    else if (string.IsNullOrEmpty(name))
    {
      throw new ArgumentException("Username cannot be null or empty.");
    }

    if (!Users.TryGetValue(id, out var user))
    {
      // Ensure that the user exists and is named correctly.
      // FIXME: We cannot differentiate between offline and invalid users.
      var matchedName = GetUserName(id);
      if (!string.IsNullOrEmpty(matchedName) && matchedName != name)
      {
        throw new ArgumentException($"User #{id} does not match User '{name}'.");
      }

      // Retrieve an existing object or create a new one in the client's cache.
      user = GetUserById(id) ?? CreateNewUser(name, id);

      // Cache the user object locally for future calls.
      //
      // Created user objects either add to the client's internal user cache or
      // return an existing user object. If an existing user object is returned,
      // it may not reflect the same user data for the given user name.
      //
      // In any case, only one instance of a user object is created per user id,
      // so any client-side updates to the user cache will be reflected in our
      // local user cache.
      Users[id] = user;
      UserIds[name] = id;

      // Set callback to remove user from cache when the client is disposed.
      RemoteClient.Disposed += (s, e) =>
      {
        Users.TryRemove(id, out _);
        UserIds.TryRemove(name, out _);
      };
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
  public static User GetUser(string name)
  {
    if (string.IsNullOrEmpty(name))
      throw new ArgumentException("Username cannot be null or empty.");

    // Attempt to retrieve the user object from the local cache.
    if (UserIds.TryGetValue(name, out var id) &&
        Users.TryGetValue(id, out var user))
      return user;

    // Otherwise, retrieve the user object from the client's UserManager.
    return GetUser(
      GetUserId(name)
        ?? throw new ArgumentException($"User '{name}' cannot be found."),
      name
    );
  }

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the user does not exist.
  /// </exception>
  public static User GetUser(int id)
  {
    if (id <= 0)
      throw new ArgumentException($"User ID must be greater than zero. Got {id}.");

    // Attempt to retrieve the user object from the local cache.
    if (Users.TryGetValue(id, out var user))
      return user;

    // Otherwise, retrieve the user object from the client's UserManager.
    return GetUser(
      id,
      GetUserName(id)
        ?? throw new ArgumentException($"User #{id} cannot be found.")
    );
  }

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
    Map<User>(/* IEnumerable<IUser> */ s_buddyUsersList,
      new Func<dynamic, User>(user => GetUser(user.Name)));

  //
  // IBuddyUsersList wrapper events
  //

  /// <summary>
  /// Occurs when a buddy user logs in.
  /// </summary>
  public static EventProxy<UserEventArgs> BuddyLoggedIn =
    new(/* IBuddyUsersList */ s_buddyUsersList, nameof(BuddyLoggedIn));
}
