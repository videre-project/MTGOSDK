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

  private static User GetUser(int id, string name) =>
    new User(
      s_userManager.CreateNewUser(id, name)
        ?? throw new Exception($"Failed to retrieve user '{name}' (#{id}).")
    );

  private static User GetUser(string name) =>
    new User(
      GetUser(
        GetUserId(name)
          ?? throw new Exception($"User '{name}' does not exist."),
        name
      )
    );

  private static User GetUser(int id) =>
    new User(
      GetUser(
        id,
        GetUserName(id)
          ?? throw new Exception($"User #{id} does not exist.")
      )
    );

  public static string GetUserName(int id) => s_userManager.GetUserName(id);

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
  /// The Login ID of the user.
  /// </summary>
  public int Id => user.Id;

  /// <summary>
  /// The display name of the user.
  /// </summary>
  public string Name = user.Name;

  /// <summary>
  /// The Catalog ID of the user's avatar.
  /// </summary>
  public int AvatarId => user.AvatarID;
}
