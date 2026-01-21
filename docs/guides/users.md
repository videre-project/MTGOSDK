# Users Guide

This guide covers the Users APIs for accessing user profiles, buddy lists, and social connections in MTGO.

## Overview

User objects represent MTGO accounts and provide access to profile information, social connections, and online status. Every player you encounter in MTGO, whether in trades, games, or chat, has a corresponding `User` object.

The `UserManager` class is your entry point for looking up users and accessing your buddy list. User lookups are relatively lightweight since profile data is cached by the client.

```csharp
using MTGOSDK.API.Users;
```

---

## Looking Up Users

You can retrieve a user by their ID or name:

```csharp
var user = UserManager.GetUser("VidereBot1");
// or via UserManager.GetUser(3136075)

Console.WriteLine($"User: {user.Name}");
Console.WriteLine($"ID: {user.Id}");
Console.WriteLine($"Is buddy: {user.IsBuddy}");
```

The `GetUser` method accepts either a username string or a numeric user ID. Looking up by ID is faster since it doesn't require a string search, but usernames are more convenient when you only have a name from chat or a trade post.

User IDs are permanent and never change, while usernames can be changed by users (though MTGO doesn't currently offer this feature, the data model supports it). When persisting user references, store the ID rather than the name for long-term reliability.

The returned `User` object includes the `IsBuddy` property, which tells you whether this user is on your buddy list. This is useful for highlighting friends in user lists, applying different trust levels, or filtering views to show only known users.

---

## Buddy List

The buddy list contains users you've added as friends. This is MTGO's built-in friends list feature:

```csharp
foreach (var buddy in UserManager.GetBuddyUsers())
{
  Console.WriteLine($"{buddy.Name} (ID: {buddy.Id})");
}
```

The `GetBuddyUsers` method returns all users on your buddy list. Each `User` object in the list has the same properties as any other user lookup, including avatar information and login status.

The buddy list is stored on MTGO's servers and syncs across all your devices. When you add someone as a buddy through the MTGO client, they appear in this list immediately. The list is useful for building friend activity displays, showing who's online, or filtering various views to prioritize known players.

User objects also expose additional state like login status and avatar information. The login status is particularly useful for building "friends online" displays, since you can check who on your buddy list is currently logged in. Avatar data includes the user's selected avatar image, which you can display in profile views or chat.

---

## Next Steps

- [Chat Guide](./chat.md) - Chat channels and messages
- [Trade Guide](./trade.md) - Trading with other users
