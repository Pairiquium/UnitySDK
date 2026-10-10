using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pairiquium.Transport;
using Pairiquium.Unity;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Wraps the gameplay relay WebSocket (<c>Backend~/arena-backend.js</c>'s dumb-relay
    /// <c>/relay</c> endpoint) for one match. This is the deliberate SDK-reuse point the sample
    /// is built around: the relay needs no protocol of its own, so there's no reason to invent a
    /// new transport - it's just <see cref="ClientWebSocketTransport"/> (already public in
    /// <c>Pairiquium.Unity</c>), the exact same interface <c>MatchmakingClient</c> uses
    /// internally, driven directly here instead of through Core's matchmaking state machine.
    ///
    /// The message envelope below (<c>pos</c>/<c>shoot</c>/<c>hit</c>/<c>defeated</c>/
    /// <c>ready</c>/<c>opponentDisconnected</c>/<c>opponentNeverJoined</c>) is this sample's own
    /// invention, not part of the Pairiquium protocol - the relay forwards any text frame
    /// verbatim and unparsed, so the two clients just need to agree on a shape between
    /// themselves. See Backend~'s README.md.
    ///
    /// Health is victim-side and local-only by design: a client never learns its opponent's
    /// exact remaining health, only a "defeated" signal once the opponent's own client decides
    /// its own player ran out - see PlayerHealth and ArenaGameController's "defeated" wiring.
    /// Hit feedback for the shooter works the same way: a shot landing is never something the
    /// shooter can determine on its own, only something the victim confirms after the fact (the
    /// "hit" message) - see ArenaGameController's OnHitConfirmed wiring.
    /// </summary>
    internal sealed class ArenaRelayConnection : IDisposable
    {
        private readonly IWebSocketTransport _transport;
        private readonly Action<Action> _dispatchToMainThread;

        public event Action OnReady;
        public event Action<float, float, float> OnOpponentPosition;
        public event Action<float, float, float> OnOpponentShoot;
        public event Action OnHitConfirmed;
        public event Action OnOpponentDefeated;
        public event Action OnOpponentDisconnected;
        public event Action OnOpponentNeverJoined;
        public event Action<Exception> OnError;

        /// <param name="dispatchToMainThread">
        /// Marshals a callback onto Unity's main thread - this class's receive loop runs on a
        /// background continuation, same as Core's MatchmakingClient, and Unity API calls (e.g.
        /// touching a Transform in a handler) aren't thread-safe. MainThreadDispatcher in
        /// Pairiquium.Unity does exactly this but is internal to that assembly, so this sample
        /// brings its own tiny version instead (see ArenaDuelSample's Update()) rather than
        /// reaching into SDK internals for a 15-line utility.
        /// </param>
        public ArenaRelayConnection(Action<Action> dispatchToMainThread)
        {
            _transport = new ClientWebSocketTransport();
            _dispatchToMainThread = dispatchToMainThread ?? throw new ArgumentNullException(nameof(dispatchToMainThread));
        }

        public async Task ConnectAsync(string relayWsUrl, CancellationToken cancellationToken)
        {
            await _transport.ConnectAsync(new Uri(relayWsUrl), cancellationToken).ConfigureAwait(false);
            _ = ReceiveLoopAsync(cancellationToken);
        }

        public Task SendPositionAsync(float x, float y, float aimAngle, CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(new { type = "pos", x, y, aimAngle });
            return _transport.SendAsync(json, cancellationToken);
        }

        public Task SendShootAsync(float x, float y, float aimAngle, CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(new { type = "shoot", x, y, aimAngle });
            return _transport.SendAsync(json, cancellationToken);
        }

        /// <summary>Tells the opponent this client's own player just ran out of health.</summary>
        public Task SendDefeatedAsync(CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(new { type = "defeated" });
            return _transport.SendAsync(json, cancellationToken);
        }

        /// <summary>
        /// Tells the opponent one of THEIR shots just landed on this client's own player. Sent
        /// by the victim, never the shooter - the shooter has no way to know a hit landed
        /// otherwise, since hit detection itself is victim-side (see Projectile).
        /// </summary>
        public Task SendHitConfirmedAsync(CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(new { type = "hit" });
            return _transport.SendAsync(json, cancellationToken);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TransportReceiveResult result = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);

                    if (result.IsClose)
                    {
                        _dispatchToMainThread(() => OnOpponentDisconnected?.Invoke());
                        return;
                    }

                    HandleMessage(result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on deliberate shutdown (ReturnToLobby/OnDestroy).
            }
            catch (Exception ex)
            {
                _dispatchToMainThread(() => OnError?.Invoke(ex));
            }
        }

        private void HandleMessage(string json)
        {
            RelayMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<RelayMessage>(json);
            }
            catch (JsonException)
            {
                return; // Malformed frame - ignore defensively, same stance as Core's message loop.
            }

            if (message == null)
            {
                return;
            }

            switch (message.Type)
            {
                case "ready":
                    _dispatchToMainThread(() => OnReady?.Invoke());
                    break;
                case "pos":
                    _dispatchToMainThread(() => OnOpponentPosition?.Invoke(message.X, message.Y, message.AimAngle));
                    break;
                case "shoot":
                    _dispatchToMainThread(() => OnOpponentShoot?.Invoke(message.X, message.Y, message.AimAngle));
                    break;
                case "hit":
                    _dispatchToMainThread(() => OnHitConfirmed?.Invoke());
                    break;
                case "defeated":
                    _dispatchToMainThread(() => OnOpponentDefeated?.Invoke());
                    break;
                case "opponentDisconnected":
                    _dispatchToMainThread(() => OnOpponentDisconnected?.Invoke());
                    break;
                case "opponentNeverJoined":
                    _dispatchToMainThread(() => OnOpponentNeverJoined?.Invoke());
                    break;
                default:
                    // Forward compatibility: ignore anything this sample version doesn't
                    // recognise yet.
                    break;
            }
        }

        public void Dispose()
        {
            _transport.Dispose();
        }

        private sealed class RelayMessage
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("x")]
            public float X { get; set; }

            [JsonProperty("y")]
            public float Y { get; set; }

            [JsonProperty("aimAngle")]
            public float AimAngle { get; set; }
        }
    }
}
