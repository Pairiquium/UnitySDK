# Pairiquium Unity SDK

[![openupm](https://img.shields.io/npm/v/com.pairiquium.sdk?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.pairiquium.sdk/)
[![openupm](https://img.shields.io/badge/dynamic/json?color=brightgreen&label=downloads&query=%24.downloads&suffix=%2Fmonth&url=https%3A%2F%2Fpackage.openupm.com%2Fdownloads%2Fpoint%2Flast-month%2Fcom.pairiquium.sdk)](https://openupm.com/packages/com.pairiquium.sdk/)

The officially supported SDK for [Pairiquium](https://pairiquium.com) — multi-tenant matchmaking-as-a-service — in Unity.

Implements the ticket/WebSocket protocol described in [`Pairiquium/API-Specs`](https://github.com/Pairiquium/API-Specs): mint a ticket from your own backend, connect, search, get matched. See [`spec/PROTOCOL.md`](spec/PROTOCOL.md) for the full protocol narrative; this SDK is pinned to the version noted in [`spec/VERSION.md`](spec/VERSION.md).

Working in this repo with an AI coding agent? See [`AGENTS.md`](AGENTS.md) first.

## How it fits together

```
Game client ──(1) log in──▶ Your backend ──(2) POST /v1/tickets, X-MaaS-API-Key──▶ Pairiquium
            ◀─(3) { ticket, wsEndpoint } ─────────────────────────────────────────
Game client ──(4) wss://<wsEndpoint>?ticket=<ticket> ──▶ Pairiquium (WebSocket)
            ◀──▶ begin_search … MATCH_FOUND

Pairiquium ──(5) POST <your NotificationWebhook> ──▶ Your backend
```

**This SDK never holds your Pairiquium API key.** It gets tickets from a provider you supply — typically `HttpTicketProvider`, which calls an endpoint on *your own backend* (your backend is the one that holds the secret key and calls Pairiquium's `POST /v1/tickets`).

## Install

### Via OpenUPM (recommended)

```
openupm add com.pairiquium.sdk
```

Or add the scoped registry manually in `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.pairiquium.sdk"]
    }
  ],
  "dependencies": {
    "com.pairiquium.sdk": "1.1.0"
  }
}
```

(Or via the Editor: Edit → Project Settings → Package Manager → Scoped Registries, using the same name/URL/scope, then add the package by name/version from the Package Manager window.)

### Via git URL

Window → Package Manager → `+` → "Add package from git URL":

```
https://github.com/Pairiquium/UnitySDK.git?path=/package#v1.1.0
```

Requires Unity 2021.3 LTS or newer. Pulls in `com.unity.nuget.newtonsoft-json` as a dependency.

The samples additionally need the **Input System** package (`com.unity.inputsystem`). It is not installed automatically: add it from Package Manager (Unity Registry) before importing a sample.

## Quick start

```csharp
using Pairiquium.Tickets;
using Pairiquium.Unity;
using UnityEngine;

public class MyMatchmaking : MonoBehaviour
{
    private MatchmakingBehaviour _matchmaking;

    void Start()
    {
        var httpClient = new System.Net.Http.HttpClient();
        var ticketProvider = new HttpTicketProvider(httpClient, new System.Uri("https://your-backend.example.com/matchmaking/ticket"));

        _matchmaking = gameObject.AddComponent<MatchmakingBehaviour>();
        _matchmaking.Initialize(ticketProvider);

        _matchmaking.OnMatchFound.AddListener(metadata =>
            Debug.Log($"Matched! matchId={metadata.MatchId} team={metadata.Team}"));
        _matchmaking.OnError.AddListener((error, isTerminal) =>
            Debug.LogWarning($"{error.Code}: {error.Message}"));

        _ = _matchmaking.BeginSearchAsync("1v1");
    }
}
```

`ticketEndpointUrl` above is **your own backend's** endpoint, not Pairiquium's. Your backend calls Pairiquium's `POST /v1/tickets` with your secret `X-MaaS-API-Key` and forwards `{ ticket, wsEndpoint }` back to the client; see [`spec/openapi.json`](spec/openapi.json) for that contract. `HttpTicketProvider` assumes your endpoint's response mirrors that same shape — if yours differs, implement `ITicketProvider` directly instead.

A fuller example, including handling `SearchSuperseded` and connection state changes, is in the **Basic Matchmaking** sample (Package Manager → Pairiquium Matchmaking SDK → Samples → Import).

The **Arena Duel** sample goes further: it completes the loop Basic Matchmaking deliberately stops short of, actually getting two matched players into a real-time 2D arena shooter against each other, via a local reference backend included with the sample (ticket proxy, the real match webhook, and a gameplay relay - see `Samples~/ArenaDuel/Backend~/README.md`).

## What `MATCH_FOUND` does and doesn't tell you

`OnMatchFound` carries only `matchId` and your `team`. It does **not** say which game server to join — your backend receives a webhook with the same `matchId` and is responsible for getting connection details to players through your own channel. Wiring that hand-off up is out of scope for this version of the SDK.

## Project layout

```
package/                 UPM package (com.pairiquium.sdk)
  Runtime/Core/           Plain C# (no UnityEngine), ships as source - MatchmakingClient, message
                          types, ITicketProvider/HttpTicketProvider, IWebSocketTransport
  Runtime/Unity/          Unity integration: MainThreadDispatcher, ClientWebSocketTransport
                          (default transport, Windows/Linux desktop), MatchmakingBehaviour
  Samples~/                Sample scenes/scripts, imported via Package Manager
tests/Pairiquium.Core.Tests/   dotnet tests against the same Core source (netstandard2.1 + C# 9)
spec/                    Vendored openapi.json/asyncapi.json/PROTOCOL.md, pinned to a tag
```

Core ships as source, not a prebuilt DLL — read it, modify it, fork it, port it to another engine.

## Platform notes

- Default transport (`ClientWebSocketTransport`) targets **Windows and Linux desktop** via `System.Net.WebSockets.ClientWebSocket`.
- **WebGL** is out of scope and excluded at the assembly level (`Pairiquium.Unity.asmdef`) — browsers don't allow raw WebSocket access the way `ClientWebSocket` needs.
- **Consoles**: `IWebSocketTransport` is the extension point. Console transports are implemented separately (under each platform's NDA) and aren't part of this public repo. Pass yours to `MatchmakingBehaviour.Initialize(ticketProvider, options, transportFactory: () => new MyTransport())` and keep the component's main-thread events and cleanup.
- Steam has no special integration here — it just runs on standard Windows/Linux (/Mac) builds. Steamworks integration, if you need it, is a separate concern from matchmaking.

## Contributing / spec changes

Don't hand-edit anything under `spec/` — those files are vendored from [`Pairiquium/API-Specs`](https://github.com/Pairiquium/API-Specs). If the SDK needs a spec change, open an issue there.
