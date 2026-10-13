# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [1.2.0] - 2026-10-13

### Added

- **Parties.** Friends can queue together. Connect first with `ConnectAsync` (on
  `MatchmakingClient` and `MatchmakingBehaviour`), form a party, then the leader's `BeginSearchAsync`
  queues everyone on that same connection. New methods: `CreatePartyAsync`, `JoinPartyAsync(code)`,
  `LeavePartyAsync`, `KickMemberAsync`, `PromoteLeaderAsync`, `InviteToPartyAsync(userId)`,
  `AcceptInviteAsync`, `DeclineInviteAsync`. The `Party` property holds the latest `PartyState`.
  Parties must be enabled per game mode in the Pairiquium dashboard.
- New events (and matching `UnityEvent`s on `MatchmakingBehaviour`): `PartyUpdated`, `PartyLeft`,
  `PartySearchCancelled`, `PartyInviteReceived`, and `SearchStarted`.
- `SearchStarted` acknowledges that a search is queued. Every party member receives it, not only the
  leader, and it is what starts a member's heartbeats.
- `MatchmakingConnectionState.Connected`: connected through `ConnectAsync` and not searching.
- Typed error codes for `SEARCH_START_FAILED` and the party errors (`ALREADY_IN_PARTY`,
  `NOT_PARTY_LEADER`, `PARTY_TOO_LARGE`, and the rest).

- Arena Duel sample: a party panel (create, join by code, invite by userId, leave) built in code, so
  the scene is unchanged. See `Samples~/ArenaDuel/Backend~/README.md`, "Parties".

### Changed

- Vendored `API-Specs` updated from `v1.0.1` to `v1.1.0` (parties and `SEARCH_STARTED`; additive).
- A search the server rejects or rolls back (`SEARCH_START_FAILED`, or a party error such as
  `NOT_PARTY_LEADER` in reply to `begin_search`) now leaves the `Searching` state instead of
  heartbeating for a search that doesn't exist.
- A connection opened with `ConnectAsync` is not reconnected when it drops, because the server removes
  a disconnected player from their party. It goes to `Closed` and `Party` is cleared. Searches started
  without `ConnectAsync` behave as before.

## [1.1.0] - 2026-10-11

### Added

- `MatchmakingBehaviour.Initialize` takes an optional `transportFactory`. Platforms without
  `System.Net.WebSockets.ClientWebSocket` (consoles) can now pass their own `IWebSocketTransport`
  and still use the component's main-thread `UnityEvent`s and cleanup, instead of dropping down to
  `MatchmakingClient` and marshalling threads themselves. Omit it to keep the default desktop
  transport; existing code is unaffected.

### Changed

- Vendored `API-Specs` updated from `v1.0.0` to `v1.0.1`. That release documents the `401`
  response of `POST /v1/tickets`; there is no protocol change.

## [1.0.1] - 2026-10-11

### Fixed

- A player who has been matched is no longer queued again. After `MATCH_FOUND` the client now
  treats the search as finished, so the connection closing afterwards (an idle timeout, or the game
  closing it late) no longer reconnects and sends `begin_search` a second time.
- Reconnect backoff and `MaxReconnectAttempts` now restart once a reconnected connection proves
  stable (it stays up for a full `HeartbeatInterval` and accepts a heartbeat). Before, the delay
  kept doubling and the attempt cap counted across the whole search, even when every reconnect
  worked. A server that accepts a connection and drops it straight away still backs off.
- The previous WebSocket is now disposed when a new search or a reconnect replaces it, and a
  transport from a connection attempt that failed is disposed too. Before, a second
  `BeginSearchAsync` on the same client left the first socket open until the server closed it.

### Changed

- `com.unity.inputsystem` is no longer a package dependency. Only the samples use it (both sample
  scenes use the Input System UI module, and Arena Duel reads the keyboard and mouse through it),
  so installing the SDK no longer adds it to your project. **If you use the samples, install the
  Input System package yourself (Package Manager, Unity Registry).** Unity may remove it on update
  if you only had it because of this SDK.

## [1.0.0] - 2026-10-10

### Added

- Core protocol client (`Pairiquium.Core`): ticket-based connect, search/cancel, PING/PONG
  heartbeat, automatic reconnect with backoff, MATCH_FOUND/error handling. Plain C#, no
  `UnityEngine` reference. Implements the protocol described in `Pairiquium/API-Specs`.
- Unity integration layer (`Pairiquium.Unity`): `MatchmakingBehaviour` (MonoBehaviour facade over
  Core), `ClientWebSocketTransport` (default desktop transport), `MainThreadDispatcher`.
- `BasicMatchmaking` sample: game mode and region selection, search/cancel, match-found/error
  feedback panels.
- `ArenaDuel` sample: completes the loop `BasicMatchmaking` stops short of - real matchmaking
  through to a live, relayed 2-player arena shooter (movement, shooting, victim-side hit
  detection, health, collision, round-end, back-to-lobby), including a runnable local reference
  backend (`Backend~/`) covering ticket proxying, the real match webhook, and the gameplay relay.
