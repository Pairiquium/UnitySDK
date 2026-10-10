using System;
using UnityEngine;

namespace Pairiquium.Samples
{
    /// <summary>
    /// A single shot. Created and destroyed entirely at runtime via <see cref="Spawn"/> - never
    /// part of the saved scene, so (unlike the player/background quads) this needs no live-Editor
    /// authoring step, just <see cref="GameObject.CreatePrimitive"/> at Play time.
    ///
    /// Visual-only unless <paramref name="isHostile"/> is true: every shot is rendered on both
    /// clients (your own outgoing shot, and the opponent's incoming one), but only the client
    /// whose player it could hit ever checks for a hit - the shooter's own copy never does. This
    /// is what "victim-side hit detection" means in practice, and it's the opposite of a naive
    /// first guess (shooter decides hits) - see ArenaGameController's relay wiring for where that
    /// split actually happens.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private const float Speed = 10f;
        private const float MaxLifetimeSeconds = 2f;
        private const float HitRadius = 0.5f;

        private static readonly Color FriendlyColor = new Color(0.9f, 0.9f, 0.3f);
        private static readonly Color HostileColor = new Color(1f, 0.2f, 0.2f);

        private Vector3 _direction;
        private float _age;
        private bool _isHostile;
        private Transform _localPlayerTransform;
        private Action _onHitLocalPlayer;

        /// <summary>
        /// Creates and configures a projectile in one call - there's no scene-authored prefab to
        /// instantiate from, so construction and initialization are inseparable here.
        /// </summary>
        public static Projectile Spawn(
            Vector3 origin, float angleDegrees, bool isHostile,
            Transform localPlayerTransform = null, Action onHitLocalPlayer = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = isHostile ? "IncomingProjectile" : "OutgoingProjectile";

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider); // Manual distance checks below, not physics - see the plan's "no physics engine" call.
            }

            var projectile = go.AddComponent<Projectile>();
            projectile.Initialize(origin, angleDegrees, isHostile, localPlayerTransform, onHitLocalPlayer);
            return projectile;
        }

        private void Initialize(
            Vector3 origin, float angleDegrees, bool isHostile,
            Transform localPlayerTransform, Action onHitLocalPlayer)
        {
            transform.position = origin;
            transform.localScale = new Vector3(0.3f, 0.3f, 1f);

            float radians = angleDegrees * Mathf.Deg2Rad;
            _direction = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
            _isHostile = isHostile;
            _localPlayerTransform = localPlayerTransform;
            _onHitLocalPlayer = onHitLocalPlayer;

            var renderer = GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = isHostile ? HostileColor : FriendlyColor;
            renderer.sharedMaterial = material;
        }

        private void Update()
        {
            transform.position += _direction * (Speed * Time.deltaTime);
            _age += Time.deltaTime;

            if (_isHostile && _localPlayerTransform != null)
            {
                float distance = Vector3.Distance(transform.position, _localPlayerTransform.position);
                if (distance <= HitRadius)
                {
                    _onHitLocalPlayer?.Invoke();
                    Destroy(gameObject);
                    return;
                }
            }

            if (_age >= MaxLifetimeSeconds)
            {
                Destroy(gameObject);
            }
        }
    }
}
