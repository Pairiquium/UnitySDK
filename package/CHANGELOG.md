# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

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
