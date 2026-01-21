# Chat Guide

This guide covers the Chat APIs for accessing MTGO chat channels and messages. These APIs let you read chat history, monitor conversations, send messages, and build chat-based features.

## Overview

MTGO uses channels for all chat communication. There are several channel types:

- **System channels**: Global announcements from MTGO (server status, maintenance notices)
- **Lobby channels**: Public chat rooms like "Main Lobby" where players can socialize
- **Game channels**: Chat and game log for active matches, combining player messages with game events
- **Private channels**: Direct messages between players

The `ChannelManager` class provides access to all channels the current user can see. The channel list updates dynamically as you join games, receive private messages, or navigate to different scenes in the client.

```csharp
using MTGOSDK.API.Chat;
```

---

## Listing Channels

You can enumerate all channels the user has access to at any given moment:

```csharp
foreach (var channel in ChannelManager.Channels)
{
  Console.WriteLine($"{channel.Name} ({channel.Type})");
  Console.WriteLine($"  Users: {channel.UserCount}");
}
```

The `Channels` collection includes every channel visible to the user. This list changes over time as games start and end, private messages arrive, or the user moves between scenes. Each channel has a `Type` property from the `ChannelType` enum indicating what kind of channel it is (System, Private, Game, etc.).

The `UserCount` property shows how many users are currently in that channel. For lobby channels, this indicates activity level. For game channels, it typically shows whether both players are connected. A game channel with only one user might indicate a disconnection.

### Finding a Specific Channel

```csharp
var lobby = ChannelManager.GetChannel("Main Lobby");
if (lobby != null)
{
  Console.WriteLine($"Found: {lobby.Name}");
  Console.WriteLine($"Users online: {lobby.UserCount}");
}
```

The `GetChannel` method searches by name and returns null if no matching channel exists. This is useful when you need to find a specific channel without iterating the full list.

Note that channel names can change or be localized in different client versions. For more robust channel discovery, consider searching by `ChannelType` instead of relying on hardcoded names.

---

## Reading Messages

Each channel maintains a message history that accumulates during the session:

```csharp
var channel = ChannelManager.Channels.First();

foreach (var msg in channel.Messages)
{
  string sender = msg.User?.Name ?? "<system>";
  Console.WriteLine($"[{msg.Timestamp}] {sender}: {msg.Text}");
}
```

The `Messages` collection contains all messages received in that channel during the current session. MTGO doesn't persist chat history across sessions, so you'll only see messages from after you logged in. Each message has a `Timestamp`, `Text`, and an optional `User` reference.

Messages from the system (announcements, game state changes, turn notifications) have a null `User` property, which is why we use the null-coalescing operator to display "<system>" as a fallback.

Game channels are particularly interesting because they contain both player chat and the game log. Turn changes, spell casts, damage, and other game actions appear as system messages in the game channel. This makes game channels useful for building replay tools, game analyzers, or stream overlays that show the action log.

---

## Sending Messages

If the channel has an active chat session, you can send messages through it:

```csharp
var channel = ChannelManager.GetChannel("Main Lobby");

channel.ChatSession?.Send("Hello!");
```

The `ChatSession` property provides access to the underlying chat session for sending messages. It's null if the channel doesn't support sending, such as:

- System channels (read-only announcements)
- Channels you've left or aren't actively connected to
- Channels where you've been muted or banned

Using the null-conditional operator (`?.`) here prevents exceptions when the session isn't available. If the send fails silently, check that the session exists before attempting to send.

Be aware that sending messages is subject to MTGO's chat rules and rate limiting. Excessive messaging may result in temporary mutes or other restrictions applied by MTGO's moderation system. Automated tools should implement rate limiting to avoid triggering these protections.

---

## Next Steps

- [Users Guide](./users.md) - User profiles and buddy lists
- [Interface Guide](./interface.md) - Toast notifications and dialogs
