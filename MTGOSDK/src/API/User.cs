/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core;

using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API;

public class User(dynamic? userInfo)
{
  //
  // UserManager wrapper methods
  //

  /// <summary>
  /// The <c>WotC.MTGO.Client.Model.Core.UserManager</c> object.
  /// <para>
  /// This class manages the client's caching and updating of user information.
  /// </para>
  /// </summary>
  private static readonly dynamic s_userManager = ObjectProvider.Get<UserManager>();

  public User(string name, bool ignoreCase = false) : this(userInfo: null) =>
    _userInfo = s_userManager.GetUser(name, ignoreCase);

  public User(int id) : this(userInfo: null) =>
    _userInfo = s_userManager.GetUser(id);

  public static string GetUserName(int id) => s_userManager.GetUserName(id);

  public static int GetUserId(string name) => s_userManager.GetUserId(name);

  //
  // UserInfo wrapper properties
  //

  /// <summary>
  /// The <c>WotC.MTGO.Common.Message.UserInfo_t</c> object.
  /// <para>
  /// This class contains basic information about the user.
  /// </para>
  /// </summary>
  private dynamic _userInfo { get; set; } = userInfo
    ?? throw new ArgumentNullException(
          $"UserManager did not return a {nameof(userInfo)} value.");

  public int Id => _userInfo.User.LoginID;

  public string Name => _userInfo.User.ScreenName;

  /// <summary>
  /// The Catalog ID of the user's avatar.
  /// </summary>
  public int AvatarId => _userInfo.AvatarID;
}
