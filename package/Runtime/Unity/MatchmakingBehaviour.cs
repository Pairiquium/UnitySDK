using System;
using System.Threading.Tasks;
using Pairiquium.Messages;
using Pairiquium.Tickets;
using Pairiquium.Transport;
using UnityEngine;
using UnityEngine.Events;

namespace Pairiquium.Unity
{
    [Serializable]
    public sealed class MatchFoundUnityEvent : UnityEvent<MatchFoundMetadata>
    {
    }

    [Serializable]
    public sealed class MatchmakingErrorUnityEvent : UnityEvent<ErrorData, bool>
    {
    }

    [Serializable]
    public sealed class MatchmakingStateChangedEvent : UnityEvent<MatchmakingConnectionState>
    {
    }

    /// <summary>
    /// MonoBehaviour wrapper around <see cref="MatchmakingClient"/>: marshals its events onto
    /// the main thread via <see cref="MainThreadDispatcher"/> and exposes them as UnityEvents so
    /// they can be wired up in the Inspector.
    ///
    /// Add this component and call <see cref="Initialize"/> with an <see cref="ITicketProvider"/>
    /// from your own game code before calling BeginSearchAsync - there's nothing to configure
    /// here that would require holding the Pairiquium API key (see HttpTicketProvider).
    /// </summary>
    public sealed class MatchmakingBehaviour : MonoBehaviour
    {
        [SerializeField] private MatchFoundUnityEvent onMatchFound = new MatchFoundUnityEvent();
        [SerializeField] private MatchmakingErrorUnityEvent onError = new MatchmakingErrorUnityEvent();
        [SerializeField] private UnityEvent onSearchSuperseded = new UnityEvent();
        [SerializeField] private MatchmakingStateChangedEvent onStateChanged = new MatchmakingStateChangedEvent();

        private MatchmakingClient _client;

        public MatchFoundUnityEvent OnMatchFound => onMatchFound;
        public MatchmakingErrorUnityEvent OnError => onError;
        public UnityEvent OnSearchSuperseded => onSearchSuperseded;
        public MatchmakingStateChangedEvent OnStateChanged => onStateChanged;

        public MatchmakingConnectionState State => _client?.State ?? MatchmakingConnectionState.Idle;

        /// <param name="ticketProvider">Where tickets come from; see <see cref="ITicketProvider"/>.</param>
        /// <param name="options">Optional heartbeat and reconnect tuning.</param>
        /// <param name="transportFactory">
        /// Optional factory for the WebSocket transport, called once per connection attempt. Leave it
        /// null on Windows and Linux desktop to use <see cref="ClientWebSocketTransport"/>. Platforms
        /// without <c>System.Net.WebSockets.ClientWebSocket</c> (consoles) pass their own
        /// <see cref="IWebSocketTransport"/> here and keep everything else this component does:
        /// main-thread events and cleanup.
        /// </param>
        public void Initialize(
            ITicketProvider ticketProvider,
            MatchmakingClientOptions options = null,
            Func<IWebSocketTransport> transportFactory = null)
        {
            if (_client != null)
            {
                throw new InvalidOperationException($"{nameof(MatchmakingBehaviour)} is already initialized.");
            }

            _client = new MatchmakingClient(
                ticketProvider,
                transportFactory ?? (() => new ClientWebSocketTransport()),
                options);
            _client.MatchFound += metadata => Dispatch(() => onMatchFound.Invoke(metadata));
            _client.ErrorReceived += (data, isTerminal) => Dispatch(() => onError.Invoke(data, isTerminal));
            _client.SearchSuperseded += () => Dispatch(() => onSearchSuperseded.Invoke());
            _client.StateChanged += state => Dispatch(() => onStateChanged.Invoke(state));
        }

        public Task BeginSearchAsync(string gameMode, string preferredRegion = null) =>
            RequireClient().BeginSearchAsync(gameMode, preferredRegion);

        public Task CancelSearchAsync() => RequireClient().CancelSearchAsync();

        public Task CloseAsync() => RequireClient().CloseAsync();

        private MatchmakingClient RequireClient()
        {
            if (_client == null)
            {
                throw new InvalidOperationException($"Call {nameof(Initialize)} before using {nameof(MatchmakingBehaviour)}.");
            }

            return _client;
        }

        private void Dispatch(Action action) => MainThreadDispatcher.Instance.Enqueue(action);

        private void OnDestroy()
        {
            _client?.Dispose();
        }
    }
}
