/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using static MTGOSDK.Core.Reflection.DLRWrapper;

using WotC.MtGO.Client.Model.Chat;
using WotC.MtGO.Client.Model.Core;
using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Users;
using static MTGOSDK.API.Events;

public static class UserManager
{
  //
  // UserManager wrapper methods
  //

  /// <summary>
  /// Manager for the client's caching and updating of user information.
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

    // Check if the user exists by the provided ID and matches the given name.
    if (GetUserName(id) != name)
    {
      throw new ArgumentException(
          $"User ID {id} does not match username '{name}'.");
    }

    IUser? user = s_userManager.GetUser(id, name, null);
    if (string.IsNullOrEmpty(user?.Name))
      throw new ArgumentException($"User '{name}' (#{id}) cannot be found.");

    return new(user);
  }

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the username is null or empty.
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the user does not exist.
  /// </exception>
  public static User GetUser(string name, bool ignoreCase = false)
  {
    if (string.IsNullOrEmpty(name))
      throw new ArgumentException("Username cannot be null or empty.");

    IUser? user = s_userManager.GetUser(name, ignoreCase);
    if (string.IsNullOrEmpty(user?.Name))
      throw new KeyNotFoundException($"User '{name}' cannot be found.");

    return new(user);
  }

  /// <summary>
  /// Retrieves a user object from the client's UserManager.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>A new User object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the user ID is less than or equal to zero.
  /// </exception>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the user does not exist.
  /// </exception>
  public static User GetUser(int id)
  {
    if (id <= 0)
      throw new ArgumentException(
          $"User ID must be greater than zero. Got {id}.");

    IUser? user = s_userManager.GetUser(id, null, null);
    if (string.IsNullOrEmpty(user?.Name))
      throw new KeyNotFoundException($"User #{id} cannot be found.");

    return new(user);
  }

  /// <summary>
  /// Retrieves the username of a user by their Login ID.
  /// </summary>
  /// <param name="id">The Login ID of the user.</param>
  /// <returns>The display name of the user.</returns>
  public static string GetUserName(int id) =>
    s_userManager.GetUserName(id);

  /// <summary>
  /// Retrieves the Login ID of a user by their username.
  /// </summary>
  /// <param name="name">The display name of the user.</param>
  /// <returns>The Login ID of the user.</returns>
  public static int? GetUserId(string name) =>
    s_userManager.GetUserId(name);

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
  public static IList<User> GetBuddyUsers() =>
    Map<IList, User>(s_buddyUsersList);

  //
  // IBuddyUsersList wrapper events
  //

  /// <summary>
  /// Occurs when a buddy user logs in.
  /// </summary>
  public static EventProxy<UserEventArgs> BuddyLoggedIn =
    new(/* IBuddyUsersList */ s_buddyUsersList, nameof(BuddyLoggedIn));
}
