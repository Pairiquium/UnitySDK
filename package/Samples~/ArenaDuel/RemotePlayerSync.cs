using UnityEngine;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Applies the opponent's position updates (received over the relay - see
    /// <see cref="ArenaRelayConnection.OnOpponentPosition"/>) to this player's transform. Purely
    /// reactive: this component never reads input, the opponent's own client is the only
    /// authority over where it is, matching this sample's client-authoritative netcode model.
    ///
    /// Lerps toward the latest reported position over the same interval
    /// <see cref="ArenaPlayerController"/> sends updates at, rather than snapping - smooths out
    /// the visible step between 10Hz updates without needing a full interpolation buffer, which
    /// would be overkill at this sample's scope.
    /// </summary>
    public sealed class RemotePlayerSync : MonoBehaviour
    {
        private const float PositionSendInterval = 0.1f; // must match ArenaPlayerController's

        private Vector3 _targetPosition;
        private float _targetAimAngleDegrees;
        private bool _hasTarget;

        public void ApplyPosition(float x, float y, float aimAngleDegrees)
        {
            _targetPosition = new Vector3(x, y, transform.position.z);
            _targetAimAngleDegrees = aimAngleDegrees;
            _hasTarget = true;
        }

        public void ResetToSpawn(Vector3 spawnPosition)
        {
            transform.position = spawnPosition;
            _hasTarget = false;
        }

        private void Update()
        {
            if (!_hasTarget)
            {
                return;
            }

            float lerpFactor = Time.deltaTime / PositionSendInterval;
            transform.position = Vector3.Lerp(transform.position, _targetPosition, lerpFactor);
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                Quaternion.Euler(0, 0, _targetAimAngleDegrees),
                lerpFactor);
        }
    }
}
