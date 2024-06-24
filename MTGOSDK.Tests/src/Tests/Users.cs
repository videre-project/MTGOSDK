/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Logging;


namespace MTGOSDK.Tests;

public class Users : UserValidationFixture
{
  [Test]
  public void Test_CurrentUser()
  {
    Log.Trace("Retrieving current user...");
    UserManager.ClearCache();
    User user = Client.CurrentUser;

    Assert.That(user.Id, Is.GreaterThan(0));
    Assert.That(user.Name, Is.Not.EqualTo(string.Empty));
    Test_GetUser(user.Id, user.Name);
  }

  [RateLimit(ms: 100)]
  [TestCase(2650356, "TheQonfused")]
  [TestCase(3136075, "VidereBot1")]
  [TestCase(3136078, "VidereBot2")]
  public void Test_GetUser(int id, string name)
  {
    //
    // MTGO only make the guarantee that fully-qualified queries will return
    // a user object. Partial queries (by id or name) will query against the
    // list of online users and may throw an exception if the user is not found.
    //
    UserManager.ClearCache();
    Log.Trace("Calling GetUser({id}, '{name}')...", id, name);
    User user = new(id, name);

    //
    // Test that the user can be found only by their id or name. If successful,
    // the MTGO client will fill in missing user data from its internal cache.
    //
    UserManager.ClearCache();
    Log.Trace("Calling GetUser({id}) and GetUser('{name}')...", id, name);
    Assert.That(id, Is.EqualTo((new User(name)).Id));
    Assert.That(name, Is.EqualTo((new User(id)).Name));
    ValidateUser(id, name, user);
  }
}

public class UserValidationFixture : BaseFixture
{
  public void ValidateUser(int id, string name, User user)
  {
    Assert.That(user.Id, Is.EqualTo(id));
    Assert.That(user.Name, Is.EqualTo(name));
    Assert.That(user.ToString(), Is.EqualTo(name));
    Assert.That(user.Avatar.Id, Is.GreaterThan(-1)); // May equal 0 if offline.

    IEnumerable<User> buddies = UserManager.GetBuddyUsers();
    Assert.That(user.IsBuddy, Is.EqualTo(buddies.Any(buddy => buddy.Id == id)));
    if (user.IsBuddy)
      Assert.That(user.IsBlocked, Is.False);

    if (user.IsLoggedIn)
    {
      ValidateAvatar(user.Avatar);
      Assert.That(user.IsGuest, name.Contains("Bot") ? Is.True : Is.False);
      Assert.That(user.LastLogin, Is.Not.EqualTo(string.Empty));
    }
  }

  public void ValidateAvatar(Avatar avatar)
  {
    Assert.That(avatar.Name, Is.Not.EqualTo(string.Empty));
    Assert.That(avatar.Card.Id, Is.GreaterThan(0));
    Assert.That(avatar.Id, Is.GreaterThan(0));
    Assert.That(avatar.View, Is.Not.EqualTo(default(Uri)));
  }
}
