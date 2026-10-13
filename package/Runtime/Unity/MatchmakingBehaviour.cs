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

    [Serializable]
    public sealed class SearchStartedUnityEvent : UnityEvent<SearchStartedData>
    {
    }

    [Serializable]
    public sealed class PartyUpdatedUnityEvent : UnityEvent<PartyState>
    {
    }

    [Serializable]
    public sealed class PartyLeftUnityEvent : UnityEvent<PartyLeftData>
    {
    }

    [Serializable]
    public sealed class PartySearchCancelledUnityEvent : UnityEvent<PartySearchCancelledData>
    {
    }

    [Serializable]
    public sealed class PartyInviteUnityEvent : UnityEvent<PartyInviteData>
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

        [SerializeField] private SearchStartedUnityEvent onSearchStarted = new SearchStartedUnityEvent();
        [SerializeField] private PartyUpdatedUnityEvent onPartyUpdated = new PartyUpdatedUnityEvent();
        [SerializeField] private PartyLeftUnityEvent onPartyLeft = new PartyLeftUnityEvent();
        [SerializeField] private PartySearchCancelledUnityEvent onPartySearchCancelled = new PartySearchCancelledUnityEvent();
        [SerializeField] private PartyInviteUnityEvent onPartyInvite = new PartyInviteUnityEvent();

        private MatchmakingClient _client;

        public MatchFoundUnityEvent OnMatchFound => onMatchFound;
        public MatchmakingErrorUnityEvent OnError => onError;
        public UnityEvent OnSearchSuperseded => onSearchSuperseded;
        public MatchmakingStateChangedEvent OnStateChanged => onStateChanged;

        public SearchStartedUnityEvent OnSearchStarted => onSearchStarted;
        public PartyUpdatedUnityEvent OnPartyUpdated => onPartyUpdated;
        public PartyLeftUnityEvent OnPartyLeft => onPartyLeft;
        public PartySearchCancelledUnityEvent OnPartySearchCancelled => onPartySearchCancelled;
        public PartyInviteUnityEvent OnPartyInvite => onPartyInvite;

        /// <summary>The party you are in, or null. Only touch it from the main thread, like the events.</summary>
        public PartyState Party => _client?.Party;

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
            _client.SearchStarted += data => Dispatch(() => onSearchStarted.Invoke(data));
            _client.PartyUpdated += party => Dispatch(() => onPartyUpdated.Invoke(party));
            _client.PartyLeft += data => Dispatch(() => onPartyLeft.Invoke(data));
            _client.PartySearchCancelled += data => Dispatch(() => onPartySearchCancelled.Invoke(data));
            _client.PartyInviteReceived += data => Dispatch(() => onPartyInvite.Invoke(data));
        }

        public Task BeginSearchAsync(string gameMode, string preferredRegion = null) =>
            RequireClient().BeginSearchAsync(gameMode, preferredRegion);

        /// <summary>
        /// Connects without searching, for forming a party first; see
        /// <see cref="MatchmakingClient.ConnectAsync"/>. Call <see cref="BeginSearchAsync"/> on the
        /// same component afterwards (the party leader's call queues the whole party).
        /// </summary>
        public Task ConnectAsync(string preferredRegion = null) => RequireClient().ConnectAsync(preferredRegion);

        public Task CreatePartyAsync() => RequireClient().CreatePartyAsync();

        public Task JoinPartyAsync(string code) => RequireClient().JoinPartyAsync(code);

        public Task LeavePartyAsync() => RequireClient().LeavePartyAsync();

        public Task KickMemberAsync(string userId) => RequireClient().KickMemberAsync(userId);

        public Task PromoteLeaderAsync(string userId) => RequireClient().PromoteLeaderAsync(userId);

        public Task InviteToPartyAsync(string userId) => RequireClient().InviteToPartyAsync(userId);

        public Task AcceptInviteAsync(string partyId) => RequireClient().AcceptInviteAsync(partyId);

        public Task DeclineInviteAsync(string partyId) => RequireClient().DeclineInviteAsync(partyId);

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
