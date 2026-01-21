# Interface Guide

This guide covers the Interface APIs for displaying notifications and dialogs in the MTGO client. These APIs let your application communicate with users through MTGO's native UI elements, making your notifications feel integrated rather than external.

## Overview

The Interface APIs provide two ways to show information to users:

- **Toast notifications**: Brief, non-blocking messages that appear in the corner and fade out automatically
- **Modal dialogs**: Blocking dialogs that require user action before the application continues

These integrate with MTGO's existing UI, so notifications appear in the same style and location as MTGO's own alerts. Users don't need to learn a new interface or look for external windows.

```csharp
using MTGOSDK.API.Interface;
```

---

## Toast Notifications

Toast notifications are useful for status updates and non-critical information that doesn't require immediate attention:

```csharp
ToastViewManager.ShowToast("Game Update", "You have joined a new match.");
```

The toast appears in MTGO's notification area (typically the bottom-right corner of the window) and dismisses automatically after a few seconds. This is the same notification system MTGO uses for its own alerts like trade requests, game invites, and chat messages, so users already understand the behavior.

Toasts are non-blocking, meaning your code continues executing immediately after calling `ShowToast`. The notification displays asynchronously and the user can ignore it, click to dismiss it, or let it fade naturally. Use toasts for informational messages where you don't need to wait for a response or confirm the user saw it.

Multiple toasts can queue up if you send several in quick succession. MTGO will display them one after another, so avoid flooding the user with too many notifications at once.

### Toast with Navigation

You can create a toast that navigates to an event when clicked:

```csharp
var tournament = EventManager.GetTournament(123456);
ToastViewManager.ShowToast(
  "Tournament Started",
  "Click to view tournament.",
  tournament
);
```

When the user clicks this toast, MTGO navigates to the tournament view automatically. This transforms the toast from a passive notification into an actionable alert that helps users get to relevant content quickly.

The navigation target can be any event object (Match, Tournament, League). This is useful for alerting users to events they might want to see, like a match starting, a round completing, or a trade request arriving. The toast becomes a shortcut to the relevant scene.

---

## Modal Dialogs

Modal dialogs block interaction until the user responds. Use them for confirmations, critical information, or any situation where you need the user to make a choice before proceeding:

```csharp
bool confirmed = DialogService.ShowModal(
  "Confirm Action",
  "Are you sure you want to leave the league?",
  okButton: "Leave",
  cancelButton: "Stay"
);

if (confirmed)
{
  Console.WriteLine("User chose to leave");
}
else
{
  Console.WriteLine("User chose to stay");
}
```

The dialog returns `true` if the user clicked the OK button, `false` for cancel. Your code blocks at this call until the user makes a choice, so this is synchronous unlike toasts. The dialog appears centered over the MTGO window and prevents interaction with the rest of the application until dismissed.

The button labels are customizable through the `okButton` and `cancelButton` parameters. It's best to use action-oriented labels that describe what each button does ("Leave"/"Stay", "Delete"/"Keep", "Confirm"/"Cancel") rather than generic labels ("OK"/"Cancel"). Specific labels reduce confusion about what each button will do.

Modal dialogs should be used sparingly. They interrupt the user's workflow and force them to respond, which can be frustrating if overused. Reserve them for:

- Destructive actions (deleting decks, leaving events)
- Irreversible changes (confirming trades, submitting deck registrations)
- Critical errors that require acknowledgment
- Important decisions that shouldn't be made accidentally

For non-critical information, prefer toasts instead.

---

## Next Steps

- [Settings Guide](./settings.md) - Reading client configuration
- [Chat Guide](./chat.md) - Chat channels and messages
