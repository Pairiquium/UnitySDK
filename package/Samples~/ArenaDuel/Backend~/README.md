# ArenaDuel backend

Local reference backend for the `ArenaDuel` sample: ticket proxy, the real Pairiquium match
webhook receiver, and a dumb relay WebSocket that forwards gameplay messages between the two
matched clients. One Node process, one port, no database, no auth beyond what Pairiquium itself
enforces on ticket minting.

**This is a local dev/demo tool, not a production pattern.** It holds no persistent state (a
restart loses every in-flight match), has no authentication on any of its own endpoints, and the
match webhook itself carries no signature yet (per `spec/PROTOCOL.md` section 5 - "do not treat
a request as authenticated on its own"). It is reachable from the public internet only because
you point a temporary tunnel at it yourself for the duration of a local test session. A real
backend would need, at minimum: webhook signature verification once Pairiquium ships it, a real
datastore instead of an in-memory `Map`, and a relay/game-server fleet instead of one process
on your laptop.

This is a stand in for local testing only, you are free to implement your own backend and use that
in place of this. This just serves as a quick route to test the Arena Dual Sample.

## Setup

```
cp .env.example .env
# fill in HTTP_URL and API_KEY in .env
npm install
npm start
```

This starts listening on `http://localhost:8787` (or whatever `PORT` you set), exposing:

- `POST /matchmaking/ticket` — point the Unity sample's `ArenaDuelSample.ticketEndpointUrl` here.
- `POST /webhooks/match-found` — point your Pairiquium game mode's `NotificationWebhook` here
  (see below — this needs a tunnel).
- `GET /matches/:matchId/connection?team=N` — used internally by the Unity client after
  `MATCH_FOUND`; you shouldn't need to call this yourself.
- `GET /relay?matchId=...&team=...` (WebSocket) — used internally by the Unity client for
  gameplay; you shouldn't need to connect to this yourself either.

## Exposing the webhook with ngrok

Pairiquium is a real cloud service — it cannot reach `localhost` on your machine directly. For
the `NotificationWebhook` to actually fire during local testing, tunnel this backend's port:

```
ngrok http 8787
```

Take the printed `https://<random>.ngrok-free.app` URL and set it (with the webhook path
appended) as the `NotificationWebhook` on the Pairiquium game mode you're testing against:

```
https://<random>.ngrok-free.app/webhooks/match-found
```

Either point an **existing** game mode's `NotificationWebhook` at this, or create a new one
(e.g. `ARENA_1V1`) first — both require your own Pairiquium dashboard/platform access, which is
outside this repo's scope to automate.

**ngrok's free tier gives you a new random subdomain every time you restart it.** That means
re-doing the step above every session you tunnel — this is expected friction for local dev, not
a bug to work around. A paid reserved subdomain removes it if it becomes annoying.

## Windows + WSL: run this backend natively on Windows, not inside WSL

If you develop on Windows with Node installed only inside WSL (a common setup), **run this
backend with a native Windows Node install instead, not from a WSL shell** - even though the
project files live on a Windows drive reachable from both.

Why: plain HTTP (the ticket proxy, the webhook, the long-poll) works fine through WSL2's
localhost-forwarding in either direction. The gameplay relay **WebSocket does not** - a Windows
process (Unity Editor) trying to open a WebSocket to a backend listening inside WSL fails with a
raw connection-refused error, even though an HTTP request to the exact same `host:port` from the
exact same process succeeds a moment earlier. This was confirmed directly: `curl`/`fetch` through
`localhost:8787` always worked; `System.Net.WebSockets.ClientWebSocket` (what this SDK's
`ClientWebSocketTransport` uses, and so what `ArenaRelayConnection` uses) to the same address
failed with `SocketException: ... actively refused`, while connecting to the WSL VM's own IP
address (`wsl hostname -I`) instead of `localhost` worked immediately. The reverse direction (a
WSL-side Node process connecting out to a backend running natively on Windows via
`localhost`) fails the same way.

The reliable fix is simply to keep both sides of every `localhost` boundary on the same OS:
install Node for Windows (`winget install OpenJS.NodeJS.LTS`, or from nodejs.org) and run
`npm install` / `npm start` from a native PowerShell/cmd prompt in this folder, not from `wsl`.
(The alternative is enabling WSL "mirrored" networking mode in `.wslconfig` plus `wsl --shutdown`,
which makes `localhost` behave identically in both directions for everything including
WebSockets - a bigger, one-time system change rather than a per-session workaround.)

## Running two clients against this backend

This backend doesn't care how many client processes connect — matching is entirely Pairiquium's
job as is the expected way to work with Pairiquium, this backend just reacts to whichever `matchId` shows up in a webhook. To test locally:

1. In Unity: **File → Build Settings → Build And Run** once to produce a standalone build.
2. Launch that same build a second time (just run the `.exe` again — a built Player has no
   single-instance restriction the way the Editor does).
3. Optionally, an Editor Play session can stand in for one of the two instances if you want live
   Console output while testing.

Both instances should point their `ticketEndpointUrl` at this backend's `/matchmaking/ticket`.
