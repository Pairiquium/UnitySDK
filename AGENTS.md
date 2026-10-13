# AGENTS.md

Orientation for an AI coding agent working in this repo. Humans: see [`README.md`](README.md)
instead - this file is deliberately terse and assumes you can read code.

## What this is

Unity SDK for Pairiquium matchmaking-as-a-service. Three layers:

```
package/Runtime/Core/    Plain C#, no UnityEngine reference. The whole protocol state machine
                         lives in MatchmakingClient. Extension points: ITicketProvider,
                         IWebSocketTransport. Ships as source, not a DLL.
package/Runtime/Unity/   Thin Unity wrapper: MatchmakingBehaviour (MonoBehaviour facade),
                         MainThreadDispatcher (marshals Core's async callbacks onto the main
                         thread), ClientWebSocketTransport (default desktop transport).
package/Samples~/        Sample scenes+scripts. Package Manager samples - see the gotcha below,
                         it is not an ordinary folder.
tests/Pairiquium.Core.Tests/   dotnet tests against Core, with Fakes/TestHarness.cs.
spec/                    Vendored from Pairiquium/API-Specs. Don't hand-edit; open an issue
                         there instead if the protocol itself needs to change.
```

Read `spec/PROTOCOL.md` before changing anything in `Core` - the XML doc comments throughout
the codebase cite specific section numbers from it, and that's the actual source of truth for
wire behavior, not the comments' paraphrase of it.

Two samples ship under `Samples~/`:

```
BasicMatchmaking/    Matchmaking only - mint a ticket, connect, search, get MATCH_FOUND. Stops
                     there deliberately; see MatchmakingSample.cs.
ArenaDuel/           Completes the loop: matchmaking -> real webhook handoff -> a local backend
                     you run yourself (Backend~/) -> a live 2-player relayed arena shooter.
                     Split into exactly two classes with one seam between them:
                       ArenaDuelSample     Matchmaking only (region/search/cancel/error UI).
                                           Mirrors BasicMatchmaking's shape. Also hosts a
                                           username gate (ArenaUsernamePanel) and the party panel
                                           (ArenaPartyPanel) - both uGUI built in code so the
                                           scene needed no edits. ArenaTicketProvider carries the
                                           chosen username through to Backend~ as the userId
                                           (instead of a random one), which is both how a client
                                           tells if it's the party leader and what a friend types
                                           into "invite by username".
                       ArenaGameController Everything from "a relay address is ready" onward -
                                           the arena, the relay connection, round lifecycle,
                                           shooting/health/collision, back-to-lobby.
                     The handoff is literally one call: ArenaDuelSample.OnMatchFound awaits
                     MatchConnectionClient for a relay address, then calls
                     ArenaGameController.EnterArenaAsync(relayWsUrl) - that line is the entire
                     boundary between "matchmaking" and "the game". The reverse trip back to the
                     lobby is an event (ArenaGameController.OnReturnedToLobby), the same
                     event-driven shape MatchmakingBehaviour itself uses. If you're extending
                     this sample, matchmaking-shaped changes belong in ArenaDuelSample,
                     gameplay-shaped changes belong in ArenaGameController - don't let gameplay
                     logic creep back into the matchmaking class the way it did before this
                     split (see git history).
```

## Verifying a change

**Core change (`package/Runtime/Core/**`):** run the real test suite, don't just read the diff.

```
cd tests/Pairiquium.Core.Tests && dotnet test
```

Fast (~1s) and deterministic. Use the existing `Fakes/` (`FakeTicketProvider`,
`FakeWebSocketTransport`) and `TestHarness` rather than hand-rolling mocks - they already
simulate the backoff/reconnect/heartbeat timing correctly. If you're adding a new failure mode
(we just did this for ticket-fetch failures - see `ConnectionFailureTests.cs` for the pattern:
`FakeTicketProvider.EnqueueFailure(...)`), add a test alongside the fix, not after.

**Anything scene/UI/GameObject-facing (`package/Samples~/**` or a consuming project's scene):**
a live Unity Editor beats hand-editing `.unity`/`.prefab` YAML every time - fileIDs and GUIDs
are easy to get wrong, and a hand-edit is invisible to a running Editor until it reimports, so a
mistake fails silently rather than loudly. Check for one first:

```
unity status                 # look for state "ready"
```

If nothing's connected and a Unity Editor is actually open for the project, the project needs
the `com.unity.pipeline` package (Unity 6.0+ only - this SDK's own floor is 2021.3, so a project
on an older Editor genuinely has no live-Editor option):

```
unity pipeline install --project-path <path>     # auto-detects the open project if omitted
unity status                                     # confirm "ready" after the Editor refocuses/recompiles
unity command                                    # list what this Editor exposes
```

Once connected, `unity command eval` runs arbitrary C# against the live Editor - this is how we
found and fixed the two bugs below. Use `get_scene_hierarchy` and `capture_game_view` to verify
visually instead of guessing from YAML.

**Only fall back to hand-editing `.unity` YAML when no Editor is reachable, and say so
explicitly.** If you do, validate before trusting it: every fileID unique within the file, every
same-file `{fileID: N}` reference (no accompanying `guid:`) resolves to a declared object, every
RectTransform's `m_Children` list is the mirror of its children's `m_Father`. A small Python/Node
script to check this is worth more than careful manual reading - see git history around the
`BasicMatchmaking.unity` samples work for a reusable pattern.

## Traps that cost real time this session - don't rediscover these

- **`GameObject.Find` returns null for an inactive GameObject.** Use
  `canvas.transform.Find("ChildName")` instead when the object might be deactivated (e.g. a
  hidden feedback panel).
- **Any live-Editor edit made while in Play Mode is discarded the moment you `editor_stop`.**
  Always exit Play Mode first, make the edit, `save_scene`, *then* re-enter Play Mode to test it.
- **`screenshot` (the generic command) only renders a camera - it misses Screen Space Overlay
  Canvas UI entirely**, which is uGUI's default and what every sample here uses. Use
  `capture_game_view` with `source=screen` (the default) instead.
- **A consuming project may have "Enter Play Mode Options" set to disable domain/scene
  reload** (a fast-iteration setting, not this repo's default but easy to hit). Under it, a
  script that does `AddComponent<X>()` + initializes it in `Start()` (like the sample does)
  won't re-run `Start()` on a second Play session, leaving a half-initialized leftover instance.
  If state looks stale/wrong after a second Play session, force a real reload rather than
  chasing a phantom bug:
  `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)`
  via `eval`, then poll `recompile_status` (takes ~20-30s).
- **`Samples~` (the trailing `~`) is invisible to Unity's AssetDatabase by design** - a
  project's "imported" copy (under its own `Assets/Samples/...`) is a one-time copy made when
  the user clicks Import in Package Manager, never a live link. Editing a file under
  `package/Samples~/` does **nothing** to an already-imported project until the user re-imports
  it (or you copy the changed file over directly and say you did).
- **Every builtin font/sprite/script reference in hand-authored scene YAML needs its real
  GUID**, not a guess. Query the live Editor for it rather than inventing one:
  `AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out localId)` via `eval`. A
  wrong-but-plausible-looking GUID produces a silently-missing reference, not an error.
- **`_ = someAsyncCall()` (fire-and-forget) silently eats any exception** that isn't already
  converted into an `ErrorReceived`/state-change event inside Core. This bit the SDK for its
  entire existence until a ticket-fetch failure was found to hang forever with zero feedback
  (now fixed - see `MatchmakingClient.ConnectAndSearchAsync`'s try/catch and
  `ErrorCodes.ConnectionFailed`). The sample's `BeginSearch()`/`CancelSearch()` now show the
  correct pattern instead: `async void` + try/catch, since a UnityEvent handler is exactly the
  one place `async void` is the right call (nothing else can ever await it). Follow that pattern
  for any new fire-and-forget call site; don't reintroduce bare `_ = ...`.
- **Adding a `Camera` via the live Editor's `add_component` does NOT auto-add
  `UniversalAdditionalCameraData`** in a URP project (that only happens through the Editor
  Inspector's own convenience logic, not the scripted API). Without it the camera silently fails
  to render - Game View shows "No cameras rendering" and, worse, the Screen Space Overlay Canvas
  UI doesn't composite either. Both sample scenes shipped without *any* camera at all for a
  while before this was caught - always `add_component Camera`, `add_component AudioListener`,
  `add_component UniversalAdditionalCameraData`, `set_tag MainCamera`, in a URP project.
- **Overwriting a `.unity` scene file on disk while the live Editor has it open can pop a
  blocking "reload?" modal** that freezes the Editor's main thread - every subsequent
  `unity command` (even `editor_status`) times out until a human dismisses it; there is no way
  to dismiss it through the Pipeline API, since dismissing it needs the same thread the API call
  is blocked waiting on. If you must overwrite an open scene from outside the Editor (e.g.
  copying a freshly-generated master-source scene over the live project's imported copy), expect
  this and warn the user you may need them to click a dialog, rather than silently retrying.
- **The Editor's entire Player Loop can freeze completely and silently** - not just a stale
  dispatcher, `Time.frameCount` itself stops incrementing - and stay frozen for an unbounded
  amount of real time regardless of `editor_focus` calls, `editor_play`, or anything else sent
  through the Pipeline API. While frozen, nothing Update()-driven runs: not just
  `MainThreadDispatcher`/this sample's own dispatch queue (so dispatched UnityEvent callbacks
  like `OnMatchFound` never fire even though the underlying Core event genuinely arrived -
  `mm.State` changes correctly, confirming it), but also gameplay scripts' own `Update()` (so a
  relay connection can succeed and even fully time out again before a single position update is
  ever processed). `editor_focus`'s "Editor focused via DockArea" success response does **not**
  guarantee this resolves - it worked most of the time this session but not always, with no
  visible difference in the response either way. **Always confirm with `Time.frameCount` across
  two calls a few seconds apart before trusting any other live-Editor result that depends on
  frames ticking** (screenshots, dispatched events, gameplay state) - a frozen counter means
  every one of those results is stale/wrong, not evidence of a real bug. If `editor_focus` and a
  few seconds' wait don't get it ticking again, the only fix found this session that reliably
  worked was asking the human to physically click into the Editor window themselves.

- **To wire a plain object-reference serialized field (not a UnityEvent) on a component via
  `eval`, use `UnityEditor.SerializedObject`/`FindProperty(name).objectReferenceValue = x`, then
  `ApplyModifiedProperties()`** - this worked reliably for every `[SerializeField] GameObject`/
  `Component` reference in the ArenaDuel sample's scripts (e.g. `arenaRoot`,
  `localPlayerController` on `ArenaGameController`).
  `set_component_properties` (the Pipeline command) rejected several of the exact same fields
  with "no serialized property" for a plain `bool`/`float` on a built-in `Camera` component in
  this same session, so for `MonoBehaviour` fields specifically, prefer `SerializedObject` over
  that command when you hit friction.
- **On Windows with Node only installed inside WSL: run any server a Windows process (the
  Unity Editor) needs to open a WebSocket to on Windows natively, not from a WSL shell** - see
  `package/Samples~/ArenaDuel/Backend~/README.md`'s "Windows + WSL" section for the full
  writeup. Plain HTTP works fine across the WSL/Windows `localhost` boundary in both directions;
  WebSocket upgrades do not, failing with a raw connection-refused error on whichever side is
  the Windows process, confirmed directly via `ClientWebSocketTransport`. Costs real debugging
  time if you don't know to check this first - a `ClientWebSocket` throwing "Unable to connect
  to the remote server" against a `localhost` target that answers plain HTTP calls just fine is
  the exact symptom, and it looks exactly like a code bug in the connecting/connected logic
  rather than an environment boundary issue.

- **Forcing a script recompile while already in Play Mode (not before entering it) can leave a
  `MonoBehaviour` that initializes itself in `Start()`/`Awake()` with uninitialized fields** -
  `AssetDatabase.Refresh()` + `RequestScriptCompilation` mid-Play reloads the domain but does not
  reliably re-run `Start()` the same way a fresh `editor_play` does, so a field a `Start()` call
  would normally set up (e.g. `ArenaDuelSample`'s `_matchmaking`, assigned via
  `_matchmaking.Initialize(ticketProvider)`) can come back null/uninitialized, surfacing later as
  a confusing runtime exception (here: `"Call Initialize before using MatchmakingBehaviour"`)
  that looks like a logic bug rather than a reload-timing one. **Deploying a script change while
  already in Play Mode needs a full `editor_stop` → `editor_play` cycle afterward, not just a
  recompile call** - the earlier "clean recompile" trap above is about fixing *stale state across
  Play sessions*, a different problem from this one (*initialization never completing in the
  current session*), and the two need different remedies even though both start with
  `RequestScriptCompilation`.
- **A multiplayer/matchmaking sample's own search can silently stop progressing without any
  error if two logically-separate "players" end up requesting different regions** - one side
  defaulting to an auto-assigned region while the other hardcodes a specific one is enough; both
  sides report a healthy `Searching` state indefinitely since neither is wrong on its own, they
  just never share a pool. Not specific to this SDK, but easy to lose an hour to when driving
  both sides of a match from scripts/automation rather than two humans who'd naturally pick the
  same displayed region - always pin both sides to the same explicit region when scripting a
  match end-to-end.

## Not yet documented here

`MatchmakingClientOptions` (backoff tuning, `MaxReconnectAttempts`) and a worked walkthrough of
implementing a custom `ITicketProvider`/`IWebSocketTransport` are planned for a broader
documentation pass, not covered in this file yet.
