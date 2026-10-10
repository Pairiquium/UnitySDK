using System;
using UnityEngine;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Tracks this client's own local player's health. Only ever attached to LocalPlayer, never
    /// RemotePlayer - each client is authoritative over whether ITS OWN player got hit, and the
    /// opponent's remaining health is never known exactly, only via the "defeated" signal once
    /// their own client decides their own player is out (see ArenaRelayConnection.OnOpponentDefeated
    /// and ArenaGameController's wiring of both directions).
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour
    {
        private const int MaxHits = 3;

        public event Action OnDefeated;

        private int _hitsTaken;

        public int RemainingHits => Mathf.Max(0, MaxHits - _hitsTaken);

        public void ResetHealth()
        {
            _hitsTaken = 0;
        }

        public void TakeHit()
        {
            if (_hitsTaken >= MaxHits)
            {
                return; // Already defeated - a projectile landing a frame late shouldn't double-fire OnDefeated.
            }

            _hitsTaken++;
            if (_hitsTaken >= MaxHits)
            {
                OnDefeated?.Invoke();
            }
        }
    }
}
