# Trade Guide

This guide covers the Trade APIs for accessing the MTGO marketplace, trade posts, and active trades. These APIs let you monitor the trading ecosystem, track your trade history, and integrate with MTGO's trading workflow.

## Overview

MTGO's trading system has three main components:

- **Trade Posts**: Public listings where users advertise cards they want to buy or sell
- **Trade Partners**: Users you've previously traded with, stored as a history for quick access
- **Trade Escrow**: The active trade session with card offers from both sides

The `TradeManager` class provides access to all of these. Unlike some other managers that require event subscriptions, trade data is available immediately through static properties.

```csharp
using MTGOSDK.API.Trade;
```

---

## Browsing Trade Posts

Trade posts appear in MTGO's Trade scene. Each post shows what the poster wants and what they're offering, along with a custom message that often includes pricing or trade terms.

```csharp
foreach (var post in TradeManager.AllPosts.Take(10))
{
  Console.WriteLine($"{post.Poster.Name}: {post.Message}");
  
  Console.WriteLine("  Wants:");
  foreach (var card in post.Wanted)
  {
    Console.WriteLine($"    {card.Quantity}x {card.Card.Name}");
  }
  
  Console.WriteLine("  Offers:");
  foreach (var card in post.Offered)
  {
    Console.WriteLine($"    {card.Quantity}x {card.Card.Name}");
  }
}
```

The `AllPosts` collection contains all visible trade posts in the current session. This collection updates as users create, modify, or remove their posts. Each post has `Wanted` and `Offered` collections containing `CardQuantityPair` objects, which pair each card with the quantity the poster wants or has available.

The `Message` property contains the poster's custom text, which typically includes pricing information, trade policies, or contact preferences. Many bot accounts use standardized message formats that you could parse programmatically.

### Your Own Post

If you have an active trade post, you can access it directly without searching through all posts:

```csharp
var myPost = TradeManager.MyPost;
if (myPost != null)
{
  Console.WriteLine($"My message: {myPost.Message}");
  Console.WriteLine($"Cards wanted: {myPost.Wanted.Count()}");
  Console.WriteLine($"Cards offered: {myPost.Offered.Count()}");
}
```

The `MyPost` property returns null if you don't have an active post. This is a convenient way to check whether your post is visible to other traders and to verify its current content matches what you expect.

---

## Trade Partners

The trade partners list shows users you've previously completed trades with, ordered by most recent interaction:

```csharp
foreach (var partner in TradeManager.TradePartners)
{
  Console.WriteLine($"{partner.Poster.Name}");
  Console.WriteLine($"  Last trade: {partner.LastTradeTime}");
}
```

Trade partners are stored locally and persist across sessions. This list is useful for finding trusted trading partners you've worked with before, or for building analytics about your trading patterns over time. The `LastTradeTime` property shows when your most recent trade with that user completed.

MTGO uses the trade partner list for its "trade with previous partner" feature, which lets you quickly initiate trades with users you've traded with before without searching for their posts.

---

## Active Trade Sessions

When you're in an active trade with another player, the `CurrentTrade` property provides access to the trade escrow. This is the live state of the negotiation, showing what each side has offered.

```csharp
var trade = TradeManager.CurrentTrade;
if (trade == null)
{
  Console.WriteLine("No active trade");
  return;
}

Console.WriteLine($"Trading with: {trade.TradePartner.Name}");
Console.WriteLine($"State: {trade.State}");
Console.WriteLine($"Accepted: {trade.IsAccepted}");

Console.WriteLine("You're offering:");
foreach (var item in trade.TradedItems)
{
  Console.WriteLine($"  {item.Quantity}x {item.Card.Name}");
}

Console.WriteLine("They're offering:");
foreach (var item in trade.PartnerTradedItems)
{
  Console.WriteLine($"  {item.Quantity}x {item.Card.Name}");
}
```

The trade escrow tracks both sides of the trade in real time. As either player adds or removes cards from the trade, the `TradedItems` and `PartnerTradedItems` collections update automatically.

The `State` property indicates where the trade is in its lifecycle: negotiating (cards being added/removed), pending confirmation (one or both players reviewing), or completed. The `IsAccepted` property becomes true when both parties have clicked "Confirm" and the trade is ready to execute.

If you're building a trading bot or automation tool, you'll want to monitor `CurrentTrade` changes to detect when trades are initiated, modified, or completed.

---

## Next Steps

- [Collection Guide](./collection.md) - Managing decks and cards
- [Users Guide](./users.md) - User profiles and buddy lists
