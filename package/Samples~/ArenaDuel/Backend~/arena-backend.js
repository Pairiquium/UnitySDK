// arena-backend.js
// Local reference backend for the ArenaDuel sample: the full "your own backend" story end to
// end, not just ticket-minting like mock-ticket-backend.js in the Pairiquium repo (same
// convention this file follows for its HTTP server/CLI-arg style). Three jobs, one process:
//
//   1. Ticket proxy        POST /matchmaking/ticket        - same shape as the existing sample
//   2. Match webhook        POST /webhooks/match-found       - the real NotificationWebhook target
//   3. Connection handoff   GET  /matches/:matchId/connection - long-poll: "where's my relay?"
//   4. Game relay           GET  /relay (WebSocket upgrade)   - dumb forward, client-authoritative
//
// This is a LOCAL DEV / DEMO tool only: no auth anywhere (the webhook itself is unsigned per
// PROTOCOL.md section 5), no persistence (everything lives in memory for the process's
// lifetime), and it is reached from the public internet only through a temporary tunnel (ngrok
// or similar) you point at it yourself. Never treat this as a production pattern - see this
// folder's README.md for what a real implementation would need on top.
//
// Usage: node arena-backend.js [options]
//   (normally run via: npm start, which loads .env first - see package.json/.env.example)
//
// Options:
//   --http-url   HTTP API URL for ticket generation (default: from env HTTP_URL)
//   --api-key    API key for ticket generation (default: from env API_KEY)
//   --port       Local port to listen on (default: 8787)

require('dotenv').config();

const http = require('http');
const crypto = require('crypto');
const { WebSocketServer } = require('ws');

const LONG_POLL_TIMEOUT_MS = 25_000;
const LONE_SOCKET_TIMEOUT_MS = 45_000;
const STALE_MATCH_SWEEP_INTERVAL_MS = 60_000;
const STALE_MATCH_TTL_MS = 5 * 60_000;

function parseArgs() {
  const args = {
    httpUrl: process.env.HTTP_URL,
    apiKey: process.env.API_KEY,
    port: parseInt(process.env.PORT, 10) || 8787,
  };

  for (let i = 2; i < process.argv.length; i++) {
    const arg = process.argv[i];
    const next = process.argv[i + 1];

    switch (arg) {
      case '--http-url':
        args.httpUrl = next;
        i++;
        break;
      case '--api-key':
        args.apiKey = next;
        i++;
        break;
      case '--port':
        args.port = parseInt(next, 10);
        i++;
        break;
      case '--help':
        console.log(`
Usage: node arena-backend.js [options]

Options:
  --http-url <url>  HTTP API URL for ticket generation
  --api-key <key>   API key for ticket generation
  --port <n>        Local port to listen on (default: 8787)

Environment variables:
  HTTP_URL, API_KEY, PORT

Point the Unity sample's ArenaDuelSample.ticketEndpointUrl at:
  http://localhost:<port>/matchmaking/ticket

Point the Pairiquium game mode's NotificationWebhook (via a tunnel, e.g. ngrok) at:
  https://<your-tunnel>/webhooks/match-found
`);
        process.exit(0);
    }
  }

  return args;
}

const config = parseArgs();

if (!config.httpUrl || !config.apiKey) {
  console.error('Error: Missing HTTP_URL or API_KEY (env, or --http-url / --api-key)');
  console.error('Run with --help for usage');
  process.exit(1);
}

function log(msg) {
  console.log(`[${new Date().toISOString()}] ${msg}`);
}

function sendJson(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(body));
}

async function readJsonBody(req) {
  const chunks = [];
  for await (const chunk of req) {
    chunks.push(chunk);
  }
  const raw = Buffer.concat(chunks).toString('utf8');
  return raw ? JSON.parse(raw) : {};
}

// ---------------------------------------------------------------------------------------------
// In-memory match/session state. Process-lifetime only, by design - this is a dev tool, not a
// service with uptime guarantees.
// ---------------------------------------------------------------------------------------------

/** @type {Map<string, { gameMode: string, participants: any[], createdAt: number }>} */
const matches = new Map();

/** @type {Map<string, Array<{ res: import('http').ServerResponse, timer: NodeJS.Timeout }>>} */
const pendingWaiters = new Map();

/** @type {Map<string, Map<number, import('ws').WebSocket>>} */
const relaySockets = new Map();

/** @type {Map<string, NodeJS.Timeout>} matchId -> "waiting for the 2nd participant" timer */
const loneSocketTimers = new Map();

function relayWsUrlFor(matchId, team) {
  return `ws://127.0.0.1:${config.port}/relay?matchId=${encodeURIComponent(matchId)}&team=${team}`;
}

function connectionPayloadFor(matchId, team) {
  const match = matches.get(matchId);
  return {
    ready: true,
    relayWsUrl: relayWsUrlFor(matchId, team),
    team,
    gameMode: match.gameMode,
  };
}

// ---------------------------------------------------------------------------------------------
// 1. Ticket proxy - POST /matchmaking/ticket
// Same contract as mock-ticket-backend.js in the Pairiquium repo: inject a userId server-side
// (the client never supplies one as far as Pairiquium is concerned), call the real
// POST /v1/tickets with the secret API key, relay the response back unmodified. The one addition
// is echoing userId back too - HttpTicketProvider only reads ticket/wsEndpoint and ignores
// unknown fields, so this is purely additive and useful for correlating logs; it is NOT how the
// webhook learns participant identity (Pairiquium already knows the userId it minted the ticket
// for).
//
// Parties need a userId a human can actually read out and type into a friend's "invite by
// username" box, so this also accepts an optional client-chosen "username" and uses it AS the
// userId when present (validated below) instead of inventing a random one. Still falls back to a
// random id if the client doesn't send one, so this stays backwards compatible with anything that
// doesn't ask the player for a name first.
//
// No uniqueness check: two clients picking the same username both mint tickets for that same
// userId. A real game wouldn't hit this (usernames would come from your own authenticated
// accounts, already unique) - acceptable for a local demo backend, not something to copy as-is.
// ---------------------------------------------------------------------------------------------

const USERNAME_PATTERN = /^[A-Za-z0-9_-]{1,24}$/;

async function handleTicketRequest(req, res) {
  let clientPreferredRegion = null;
  let clientUsername = null;
  try {
    const parsedBody = await readJsonBody(req);
    clientPreferredRegion = parsedBody.preferredRegion || null;
    clientUsername = parsedBody.username || null;
  } catch {
    // Malformed/empty body from the client - fine, just means no region override/username.
  }

  if (clientUsername !== null && !USERNAME_PATTERN.test(clientUsername)) {
    sendJson(res, 400, { error: 'username must be 1-24 characters, letters/numbers/-/_ only' });
    return;
  }

  const userId = clientUsername || `unity-${crypto.randomUUID().slice(0, 8)}`;
  const body = { userId };
  if (clientPreferredRegion) {
    body.preferredRegion = clientPreferredRegion;
  }

  const ticketUrl = `${config.httpUrl}/v1/tickets`;
  log(`POST ${ticketUrl} ${JSON.stringify(body)}`);

  try {
    const response = await fetch(ticketUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-MaaS-API-Key': config.apiKey,
      },
      body: JSON.stringify(body),
    });

    const text = await response.text();
    log(`-> ${response.status}: ${text}`);

    if (!response.ok) {
      res.writeHead(response.status, { 'Content-Type': 'application/json' });
      res.end(text);
      return;
    }

    const parsed = JSON.parse(text);
    sendJson(res, response.status, { ...parsed, userId });
  } catch (err) {
    log(`Error: ${err.message}`);
    sendJson(res, 502, { error: err.message });
  }
}

// ---------------------------------------------------------------------------------------------
// 2. Match webhook - POST /webhooks/match-found
// The literal NotificationWebhook target, reached through a tunnel. Must dedupe on matchId
// (delivery is at-least-once per PROTOCOL.md section 5) and ack fast - everything here is
// synchronous in-memory work, comfortably inside the 5s/2xx requirement.
// ---------------------------------------------------------------------------------------------

async function handleWebhookRequest(req, res) {
  let payload;
  try {
    payload = await readJsonBody(req);
  } catch (err) {
    sendJson(res, 400, { error: `invalid JSON body: ${err.message}` });
    return;
  }

  const { gameMode, matchId, participants } = payload;
  if (!matchId || !Array.isArray(participants)) {
    sendJson(res, 400, { error: 'expected { gameMode, matchId, participants[] }' });
    return;
  }

  if (matches.has(matchId)) {
    // Retry of a delivery we already processed - idempotent no-op, still ack 2xx.
    log(`webhook: duplicate delivery for matchId=${matchId}, ignoring`);
    sendJson(res, 200, { received: true, duplicate: true });
    return;
  }

  log(`webhook: matchId=${matchId} gameMode=${gameMode} participants=${JSON.stringify(participants)}`);
  matches.set(matchId, { gameMode, participants, createdAt: Date.now() });

  const waiters = pendingWaiters.get(matchId) || [];
  pendingWaiters.delete(matchId);
  for (const waiter of waiters) {
    clearTimeout(waiter.timer);
    const team = waiter.team;
    sendJson(waiter.res, 200, connectionPayloadFor(matchId, team));
  }

  sendJson(res, 200, { received: true, duplicate: false });
}

// ---------------------------------------------------------------------------------------------
// 3. Connection handoff - GET /matches/:matchId/connection?team=N
// Long-poll: a client's matchmaking WebSocket (MatchmakingBehaviour) only carries matchId+team
// from MATCH_FOUND, never a game server address (see PROTOCOL.md section 4 - "your own
// channel"). This is that channel. Long-poll rather than short-poll or a push channel: a push
// channel is circular (the client has no connection yet to be pushed on - that's the problem
// being solved), and short-poll either wastes round trips on an arbitrary interval or spams
// this process's logs, which actively hurts while hand-debugging tunnel/webhook timing.
// ---------------------------------------------------------------------------------------------

function handleConnectionRequest(req, res, matchId, teamParam) {
  const team = parseInt(teamParam, 10);
  if (Number.isNaN(team)) {
    sendJson(res, 400, { error: `invalid team query param: ${teamParam}` });
    return;
  }

  const match = matches.get(matchId);
  if (match) {
    const participant = match.participants.find((p) => p.Team === team);
    if (!participant) {
      sendJson(res, 400, { error: `no participant for matchId ${matchId} team ${team}` });
      return;
    }
    sendJson(res, 200, connectionPayloadFor(matchId, team));
    return;
  }

  // Not here yet - hold the request open until the webhook lands or we time out. The client is
  // expected to immediately re-issue another long-poll on a { ready: false } response, in a loop
  // with no overall deadline: a real webhook retry (e.g. a cold-starting tunnel) can land well
  // after any single long-poll cycle's timeout.
  const timer = setTimeout(() => {
    const waiters = pendingWaiters.get(matchId);
    if (!waiters) return;
    const remaining = waiters.filter((w) => w.res !== res);
    if (remaining.length > 0) {
      pendingWaiters.set(matchId, remaining);
    } else {
      pendingWaiters.delete(matchId);
    }
    sendJson(res, 200, { ready: false });
  }, LONG_POLL_TIMEOUT_MS);

  const waiter = { res, timer, team };
  const waiters = pendingWaiters.get(matchId) || [];
  waiters.push(waiter);
  pendingWaiters.set(matchId, waiters);

  req.on('close', () => {
    clearTimeout(timer);
    const current = pendingWaiters.get(matchId);
    if (current) {
      const remaining = current.filter((w) => w !== waiter);
      if (remaining.length > 0) {
        pendingWaiters.set(matchId, remaining);
      } else {
        pendingWaiters.delete(matchId);
      }
    }
  });
}

// ---------------------------------------------------------------------------------------------
// HTTP routing
// ---------------------------------------------------------------------------------------------

const CONNECTION_PATH_PATTERN = /^\/matches\/([^/]+)\/connection$/;

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${config.port}`);

  if (req.method === 'POST' && url.pathname === '/matchmaking/ticket') {
    handleTicketRequest(req, res);
    return;
  }

  if (req.method === 'POST' && url.pathname === '/webhooks/match-found') {
    handleWebhookRequest(req, res);
    return;
  }

  const connectionMatch = CONNECTION_PATH_PATTERN.exec(url.pathname);
  if (req.method === 'GET' && connectionMatch) {
    handleConnectionRequest(req, res, connectionMatch[1], url.searchParams.get('team'));
    return;
  }

  sendJson(res, 404, {
    error: 'Not found. POST /matchmaking/ticket, POST /webhooks/match-found, GET /matches/:matchId/connection, GET /relay (WebSocket)',
  });
});

// ---------------------------------------------------------------------------------------------
// 4. Game relay - GET /relay?matchId=...&team=... (WebSocket upgrade)
// The entire "dumb relay" netcode model: forward any text frame from one socket to the other,
// completely unparsed. No server-side simulation, no hit resolution - each client decides
// locally whether it got hit (victim-side, not shooter-side - see the sample's PlayerHealth.cs).
// ---------------------------------------------------------------------------------------------

const wss = new WebSocketServer({ noServer: true });

function rejectUpgrade(socket, status, message) {
  socket.write(`HTTP/1.1 ${status} ${message}\r\nConnection: close\r\n\r\n`);
  socket.destroy();
}

function cleanupMatch(matchId) {
  matches.delete(matchId);
  relaySockets.delete(matchId);
  const loneTimer = loneSocketTimers.get(matchId);
  if (loneTimer) {
    clearTimeout(loneTimer);
    loneSocketTimers.delete(matchId);
  }
  log(`relay: cleaned up matchId=${matchId}`);
}

server.on('upgrade', (req, socket, head) => {
  const url = new URL(req.url, `http://localhost:${config.port}`);
  if (url.pathname !== '/relay') {
    rejectUpgrade(socket, 404, 'Not Found');
    return;
  }

  const matchId = url.searchParams.get('matchId');
  const team = parseInt(url.searchParams.get('team'), 10);
  const match = matches.get(matchId);

  if (!match || Number.isNaN(team) || !match.participants.some((p) => p.Team === team)) {
    rejectUpgrade(socket, 400, 'Bad Request');
    return;
  }

  const sockets = relaySockets.get(matchId) || new Map();
  if (sockets.has(team)) {
    // Reconnect mid-match is explicitly out of scope for this sample - a second socket for a
    // team already connected is treated as a conflict, not a replacement.
    rejectUpgrade(socket, 409, 'Conflict');
    return;
  }

  wss.handleUpgrade(req, socket, head, (ws) => {
    sockets.set(team, ws);
    relaySockets.set(matchId, sockets);
    log(`relay: matchId=${matchId} team=${team} connected (${sockets.size}/2)`);

    if (sockets.size === 1) {
      const timer = setTimeout(() => {
        if (sockets.size < 2) {
          log(`relay: matchId=${matchId} team=${team} - opponent never joined, closing`);
          ws.send(JSON.stringify({ type: 'opponentNeverJoined' }));
          ws.close();
        }
      }, LONE_SOCKET_TIMEOUT_MS);
      loneSocketTimers.set(matchId, timer);
    } else {
      const loneTimer = loneSocketTimers.get(matchId);
      if (loneTimer) {
        clearTimeout(loneTimer);
        loneSocketTimers.delete(matchId);
      }
      for (const peer of sockets.values()) {
        peer.send(JSON.stringify({ type: 'ready' }));
      }
    }

    ws.on('message', (data) => {
      for (const [otherTeam, peer] of sockets) {
        if (otherTeam !== team && peer.readyState === peer.OPEN) {
          peer.send(data.toString());
        }
      }
    });

    ws.on('close', () => {
      sockets.delete(team);
      log(`relay: matchId=${matchId} team=${team} disconnected (${sockets.size}/2 remaining)`);
      for (const peer of sockets.values()) {
        if (peer.readyState === peer.OPEN) {
          peer.send(JSON.stringify({ type: 'opponentDisconnected' }));
        }
      }
      if (sockets.size === 0) {
        cleanupMatch(matchId);
      }
    });
  });
});

// Backstop for matches that got a webhook but whose clients never connected to the relay at
// all (e.g. a client crashed right after MATCH_FOUND) - the normal cleanup path above only
// fires once relay sockets actually existed and then closed.
setInterval(() => {
  const now = Date.now();
  for (const [matchId, match] of matches) {
    const sockets = relaySockets.get(matchId);
    const hasAnyRelayActivity = sockets && sockets.size > 0;
    if (!hasAnyRelayActivity && now - match.createdAt > STALE_MATCH_TTL_MS) {
      log(`sweep: matchId=${matchId} abandoned (no relay connection within TTL)`);
      cleanupMatch(matchId);
    }
  }
}, STALE_MATCH_SWEEP_INTERVAL_MS).unref();

server.listen(config.port, () => {
  log(`Listening on http://localhost:${config.port}`);
  log(`  POST /matchmaking/ticket`);
  log(`  POST /webhooks/match-found   <- point your Pairiquium game mode's NotificationWebhook here (via a tunnel)`);
  log(`  GET  /matches/:matchId/connection?team=N`);
  log(`  GET  /relay?matchId=...&team=...   (WebSocket)`);
  log(`Forwarding tickets to ${config.httpUrl}/v1/tickets`);
});

process.on('SIGINT', () => {
  log('Shutting down.');
  process.exit(0);
});
