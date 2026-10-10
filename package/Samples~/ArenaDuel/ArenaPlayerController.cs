using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Local player movement for the arena: WASD/arrow keys to move, mouse to aim, clamped to a
    /// fixed arena rectangle. Sends its position to the opponent over the relay at a fixed tick
    /// rate rather than every frame - this is the client-authoritative, dumb-relay netcode model
    /// this sample deliberately chose: no server-side simulation, each client just reports where
    /// it is.
    ///
    /// Uses the new Input System directly (<see cref="Keyboard"/>/<see cref="Mouse"/>) rather
    /// than the legacy <c>UnityEngine.Input</c> class - this project's Player Settings have
    /// Active Input Handling set to the new package (required for InputSystemUIInputModule on
    /// the EventSystem), and the legacy Input class throws under that setting.
    /// </summary>
    public sealed class ArenaPlayerController : MonoBehaviour
    {
        private const float MoveSpeed = 4f;
        private const float ArenaHalfWidth = 8f;
        private const float ArenaHalfHeight = 5f;
        private const float PositionSendInterval = 0.1f; // 10Hz

        // Sum of both players' quad half-widths (0.5 each, see the scene's LocalPlayer/RemotePlayer
        // scale) plus a little breathing room - a simple hard stop, not a physics engine, per the
        // plan's "manual transform math + distance checks" call.
        private const float PlayerCollisionDistance = 1.1f;

        private ArenaRelayConnection _relay;
        private Camera _arenaCamera;
        private Transform _remotePlayerTransform;
        private CancellationToken _cancellationToken;
        private float _timeSinceLastSend;
        private float _aimAngleDegrees;

        internal void Initialize(
            ArenaRelayConnection relay, Camera arenaCamera, Transform remotePlayerTransform,
            CancellationToken cancellationToken)
        {
            _relay = relay;
            _arenaCamera = arenaCamera;
            _remotePlayerTransform = remotePlayerTransform;
            _cancellationToken = cancellationToken;
        }

        private void Update()
        {
            if (_relay == null)
            {
                return; // Not initialized yet, or the match ended.
            }

            Move();
            Aim();
            Shoot();
            TrySendPosition();
        }

        private void Move()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            Vector2 direction = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) direction.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) direction.y -= 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) direction.x -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) direction.x += 1;

            if (direction.sqrMagnitude > 0f)
            {
                direction.Normalize();
                Vector3 pos = transform.position;
                Vector3 newPos = pos;
                newPos.x = Mathf.Clamp(pos.x + direction.x * MoveSpeed * Time.deltaTime, -ArenaHalfWidth, ArenaHalfWidth);
                newPos.y = Mathf.Clamp(pos.y + direction.y * MoveSpeed * Time.deltaTime, -ArenaHalfHeight, ArenaHalfHeight);

                // This client's own perception of where the opponent currently is (RemotePlayerSync's
                // interpolated position, not their true authoritative one) is the only thing either
                // client has to collide against in this netcode model - good enough for a sample,
                // not perfectly consistent between both players' screens.
                bool blockedByOpponent = _remotePlayerTransform != null
                    && Vector3.Distance(newPos, _remotePlayerTransform.position) < PlayerCollisionDistance;

                if (!blockedByOpponent)
                {
                    transform.position = newPos;
                }
            }
        }

        private async void Shoot()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Projectile.Spawn(transform.position, _aimAngleDegrees, isHostile: false);

            // async void for the same reason TrySendPosition() below is: fired from Update(),
            // nothing can await it, and a dropped "shoot" send isn't worth surfacing to the
            // player (they already see their own shot fire locally) - the connection's own
            // OnError already reports real failures.
            try
            {
                await _relay.SendShootAsync(transform.position.x, transform.position.y, _aimAngleDegrees, _cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (System.Exception)
            {
            }
        }

        private void Aim()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _arenaCamera == null)
            {
                return;
            }

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 worldPos = _arenaCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -_arenaCamera.transform.position.z));
            Vector2 toMouse = (Vector2)worldPos - (Vector2)transform.position;
            if (toMouse.sqrMagnitude > 0.0001f)
            {
                _aimAngleDegrees = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;
            }
        }

        private async void TrySendPosition()
        {
            _timeSinceLastSend += Time.deltaTime;
            if (_timeSinceLastSend < PositionSendInterval)
            {
                return;
            }
            _timeSinceLastSend = 0f;

            // async void here follows the same rule as ArenaDuelSample/ArenaGameController's own
            // handlers: this is fired from Update(), nothing can ever await it, so this
            // try/catch is the only place a failure is observable - an uncaught exception here
            // would otherwise crash silently (or spam) every ~100ms. A single dropped position
            // update isn't worth surfacing to the player (the next tick supersedes it), so this
            // intentionally just swallows rather than showing an error - ArenaRelayConnection's
            // own receive loop already reports real connection failures via OnError.
            try
            {
                await _relay.SendPositionAsync(transform.position.x, transform.position.y, _aimAngleDegrees, _cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (System.Exception)
            {
            }
        }
    }
}
