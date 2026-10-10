using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Everything that happens AFTER matchmaking has handed off a live relay connection: owns
    /// the arena, the gameplay relay, round lifecycle (shooting, health, round-end for every
    /// reason - defeat, victory, opponent disconnect/never-joined) and the return trip to the
    /// lobby. <see cref="ArenaDuelSample"/> is the only thing that talks to this class, and does
    /// so through exactly two seams: it calls <see cref="EnterArenaAsync"/> once a relay address
    /// is ready, and listens for <see cref="OnReturnedToLobby"/> to know when to show its own
    /// lobby UI again - the same event-driven shape <c>MatchmakingBehaviour</c> itself uses for
    /// its own handoffs, deliberately, so the two feel like the same pattern rather than two
    /// different ones bolted together.
    ///
    /// Shooting is victim-side: each client only ever decides whether ITS OWN player got hit
    /// (see Projectile/PlayerHealth), never the opponent's, and only ever learns it landed a hit
    /// on the opponent via an explicit "hit" confirmation the opponent's client sends back.
    /// </summary>
    public sealed class ArenaGameController : MonoBehaviour
    {
        private static readonly Vector3 LocalPlayerSpawn = new Vector3(-3, 0, 0);
        private static readonly Vector3 RemotePlayerSpawn = new Vector3(3, 0, 0);

        [SerializeField] private GameObject lobbyCanvas;
        [SerializeField] private GameObject arenaRoot;
        [SerializeField] private Camera arenaCamera;
        [SerializeField] private ArenaPlayerController localPlayerController;
        [SerializeField] private RemotePlayerSync remotePlayerSync;
        [SerializeField] private PlayerHealth localPlayerHealth;
        [SerializeField] private Text arenaStatusText;
        [SerializeField] private GameObject roundEndPanel;
        [SerializeField] private Text roundEndText;
        [SerializeField] private Text hitIndicatorText;

        /// <summary>Fired once the player leaves the arena via BackToLobby - ArenaDuelSample's
        /// cue to show its own lobby UI again.</summary>
        public event Action OnReturnedToLobby;

        private ArenaRelayConnection _relayConnection;
        private CancellationTokenSource _arenaCts;
        private bool _roundEnded;
        private float _hitIndicatorSecondsRemaining;

        // Drains on Update() - this sample's own tiny version of Pairiquium.Unity's
        // MainThreadDispatcher, which is internal to that assembly. ArenaRelayConnection's
        // receive loop runs on a background continuation and needs a way back onto the main
        // thread before touching Unity objects (e.g. RemotePlayerSync's Transform); duplicating
        // this ~10-line utility here is simpler and more honest than reaching into SDK
        // internals for it.
        private readonly Queue<Action> _mainThreadActions = new Queue<Action>();
        private readonly object _mainThreadActionsGate = new object();

        private void Update()
        {
            if (_hitIndicatorSecondsRemaining > 0f)
            {
                _hitIndicatorSecondsRemaining -= Time.deltaTime;
                if (_hitIndicatorSecondsRemaining <= 0f && hitIndicatorText != null)
                {
                    hitIndicatorText.text = string.Empty;
                }
            }

            while (true)
            {
                Action action;
                lock (_mainThreadActionsGate)
                {
                    if (_mainThreadActions.Count == 0)
                    {
                        return;
                    }
                    action = _mainThreadActions.Dequeue();
                }
                action();
            }
        }

        private void DispatchToMainThread(Action action)
        {
            lock (_mainThreadActionsGate)
            {
                _mainThreadActions.Enqueue(action);
            }
        }

        // Called by ArenaDuelSample.OnMatchFound once MatchConnectionClient has a real relay
        // address - the one and only entry point into this class. Not async void: the caller
        // awaits it inside its own try/catch, so exceptions here are already observed there; a
        // second try/catch here would just duplicate it.
        public async Task EnterArenaAsync(string relayWsUrl)
        {
            localPlayerController.transform.position = LocalPlayerSpawn;
            remotePlayerSync.ResetToSpawn(RemotePlayerSpawn);
            localPlayerHealth.ResetHealth();
            localPlayerController.enabled = true;
            roundEndPanel.SetActive(false);
            _roundEnded = false;
            _hitIndicatorSecondsRemaining = 0f;
            if (hitIndicatorText != null)
            {
                hitIndicatorText.text = string.Empty;
            }

            _arenaCts?.Cancel();
            _arenaCts = new CancellationTokenSource();

            // Fresh subscription every round: a stale OnDefeated handler from a previous round
            // would otherwise fire SendDefeatedAsync against a CancellationToken that's already
            // cancelled, or worse, double-fire into whichever ArenaRelayConnection is current.
            localPlayerHealth.OnDefeated -= HandleLocalPlayerDefeated;
            localPlayerHealth.OnDefeated += HandleLocalPlayerDefeated;

            _relayConnection?.Dispose();
            _relayConnection = new ArenaRelayConnection(DispatchToMainThread);
            _relayConnection.OnReady += () => SetArenaStatus("Go!");
            _relayConnection.OnOpponentPosition += (x, y, aimAngle) => remotePlayerSync.ApplyPosition(x, y, aimAngle);
            _relayConnection.OnOpponentShoot += (x, y, aimAngle) =>
            {
                Projectile.Spawn(
                    new Vector3(x, y, 0f), aimAngle, isHostile: true,
                    localPlayerController.transform, () =>
                    {
                        localPlayerHealth.TakeHit();
                        NotifyOpponentOfHit();
                    });
            };
            _relayConnection.OnHitConfirmed += ShowHitIndicator;
            _relayConnection.OnOpponentDefeated += () => ShowRoundEnd("You win!");
            _relayConnection.OnOpponentDisconnected += () => ShowRoundEnd("Opponent disconnected.");
            _relayConnection.OnOpponentNeverJoined += () => ShowRoundEnd("Opponent never connected.");
            _relayConnection.OnError += ex =>
            {
                Debug.LogException(ex);
                SetArenaStatus($"Relay error: {ex.Message}");
            };

            await _relayConnection.ConnectAsync(relayWsUrl, _arenaCts.Token);

            // Only swap the UI over once the relay connection actually succeeded - if it threw,
            // control returns to the caller's catch, which shows its own error UI; lobbyCanvas
            // needs to still be visible underneath that, not hidden under an arena we never
            // actually entered.
            lobbyCanvas.SetActive(false);
            arenaRoot.SetActive(true);
            SetArenaStatus("Waiting for opponent...");

            localPlayerController.Initialize(_relayConnection, arenaCamera, remotePlayerSync.transform, _arenaCts.Token);
        }

        // Wired to PlayerHealth.OnDefeated. async void because this IS a top-level event
        // callback a C# event handler can't await; the opponent needs to hear about this even
        // though the local round is already visually over, so the send isn't skippable the way
        // ArenaPlayerController's per-tick sends are - but the round-end UI shouldn't wait on it
        // either, so ShowRoundEnd happens first and the send still gets its own try/catch.
        private async void HandleLocalPlayerDefeated()
        {
            ShowRoundEnd("You lose!");
            try
            {
                await _relayConnection.SendDefeatedAsync(_arenaCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // Fired from inside a Projectile's hit callback (see OnOpponentShoot above), not from a
        // UnityEvent, so this is a plain fire-and-forget helper rather than a top-level handler -
        // same async void + try/catch shape as the rest of this file's sends regardless, since
        // nothing here can await it either.
        private async void NotifyOpponentOfHit()
        {
            try
            {
                await _relayConnection.SendHitConfirmedAsync(_arenaCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // Wired to ArenaRelayConnection.OnHitConfirmed - a shot THIS client fired landed on the
        // opponent. Purely cosmetic: a short-lived text flash, cleared by the countdown in
        // Update(). Not gated by _roundEnded since a hit confirmation arriving right as the
        // round ends is still a true, useful thing to show.
        private void ShowHitIndicator()
        {
            if (hitIndicatorText == null)
            {
                return;
            }
            hitIndicatorText.text = "Hit!";
            _hitIndicatorSecondsRemaining = 0.5f;
        }

        // First call wins: once the round has ended one way, a second trigger arriving a moment
        // later (e.g. the opponent's client closing its relay connection right after sending
        // "defeated") shouldn't silently overwrite the real reason with a less informative one.
        private void ShowRoundEnd(string message)
        {
            if (_roundEnded)
            {
                return;
            }
            _roundEnded = true;

            localPlayerController.enabled = false;
            SetArenaStatus(message);
            roundEndText.text = message;
            roundEndPanel.SetActive(true);
        }

        // Wired to the round-end panel's "Back to Lobby" button.
        public void BackToLobby()
        {
            _arenaCts?.Cancel();
            _relayConnection?.Dispose();
            _relayConnection = null;

            // A Projectile still mid-flight when the player leaves isn't destroyed by anything
            // else - it would otherwise keep ticking, and a hostile one could still call
            // localPlayerHealth.TakeHit() after _relayConnection is already gone, which
            // HandleLocalPlayerDefeated only tolerates via its own try/catch rather than this
            // being a state that should exist at all.
            foreach (Projectile projectile in FindObjectsByType<Projectile>(FindObjectsInactive.Include))
            {
                Destroy(projectile.gameObject);
            }

            roundEndPanel.SetActive(false);
            arenaRoot.SetActive(false);
            lobbyCanvas.SetActive(true);

            OnReturnedToLobby?.Invoke();
        }

        private void SetArenaStatus(string text)
        {
            if (arenaStatusText != null)
            {
                arenaStatusText.text = text;
            }
        }

        private void OnDestroy()
        {
            _arenaCts?.Cancel();
            _relayConnection?.Dispose();
        }
    }
}
