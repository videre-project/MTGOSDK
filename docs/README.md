# MTGOSDK Documentation

Documentation for the MTGOSDK codebase, organized into three tiers:

| Section | Description | Audience |
| ------- | ----------- | -------- |
| [Guides](./guides/README.md) | Task-oriented tutorials | End users |
| [Reference](./reference/README.md) | Per-namespace API documentation | End users |
| [Architecture](./architecture/README.md) | SDK internals and design decisions | Contributors |

## Quick Links

- [Getting Started](./getting-started.md) - Installation and setup
- [FAQ](./FAQ.md) - Frequently asked questions
- [Changelog](../CHANGELOG.md) - Version history

## Guides

- [Collection](./guides/collection.md) - Managing decks, cards, and binders
- [Play](./guides/play.md) - Monitoring matches, games, tournaments, and leagues
- [Games](./guides/games.md) - In-game state tracking (zones, cards, actions)
- [History](./guides/history.md) - Accessing completed matches and tournaments
- [Chat](./guides/chat.md) - Interacting with chat channels and messages
- [Users](./guides/users.md) - Managing user profiles and buddy lists
- [Trade](./guides/trade.md) - Accessing trade posts and managing trades
- [Settings](./guides/settings.md) - Reading client configuration
- [Interface](./guides/interface.md) - Displaying toasts and dialogs

## Reference

- [Client](./reference/client.md) - Manages MTGO process and session
- [ObjectProvider](./reference/object-provider.md) - Retrieves globally registered singleton objects
- [Connection Lifecycle](./reference/connection-lifecycle.md) - Crash recovery and reconnection patterns
- [ClickOnce](./reference/clickonce.md) - MTGO deployment, installation, and CI/CD
- [Debugging](./reference/debugging.md) - Log files, traces, and troubleshooting

## Architecture

- [DLRWrapper](./architecture/dlr-wrapper.md) - Type-safe interface binding for dynamic remote objects
- [RemoteClient](./architecture/remote-client.md) - Low-level class for accessing remote objects
- [Logging](./architecture/logging.md) - Structured logging with automatic caller detection
- [Events](./architecture/events.md) - Event proxies and hooks for remote event subscription
- [Threading](./architecture/threading.md) - Task scheduling and background thread management
- [Serialization](./architecture/serialization.md) - Batch property fetching for performance
- [Type Compilation](./architecture/type-compilation.md) - Runtime proxy generation
- [Memory](./architecture/memory.md) - GC coordination and remote object references
