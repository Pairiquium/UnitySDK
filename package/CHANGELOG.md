# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

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
