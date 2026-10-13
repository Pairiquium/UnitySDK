using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pairiquium.Messages;
using Pairiquium.Tickets;
using Pairiquium.Transport;

namespace Pairiquium
{
    /// <summary>
    /// Drives one matchmaking search over one logical connection: connects with a fresh ticket,
    /// sends <c>begin_search</c>, answers <c>PING</c> with <c>pong</c>, sends heartbeats while
    /// searching, and reconnects with a fresh ticket after an unexpected drop. See PROTOCOL.md.
    /// </summary>
    public sealed class MatchmakingClient : IDisposable
    {
        private static readonly JsonSerializerSettings ServerMessageSettings = new JsonSerializerSettings
        {
            Converters = { new ServerMessageConverter() }
        };

        private readonly ITicketProvider _ticketProvider;
        private readonly Func<IWebSocketTransport> _transportFactory;
        private readonly MatchmakingClientOptions _options;

        private CancellationTokenSource _lifetimeCts;
        private CancellationTokenSource _heartbeatCts;
        private IWebSocketTransport _transport;
        private string _currentGameMode;
        private string _currentPreferredRegion;
        private int _reconnectAttempts;
        private volatile bool _cancelRequested;

        // True while the current connection was opened by ConnectAsync (not by BeginSearchAsync):
        // it exists to form and manage a party in, so BeginSearchAsync reuses it.
        private bool _lobbyConnection;

        // True from sending begin_search until the server answers (SEARCH_STARTED) or the search
        // ends, so an ERROR that rejects the search can be told apart from one about something else.
        private bool _beginSearchPending;

        public MatchmakingConnectionState State { get; private set; } = MatchmakingConnectionState.Idle;

        /// <summary>
        /// The party this connection is in, as of the last <c>PARTY_UPDATE</c>; null when not in
        /// one. Cleared when the connection is lost: the server removes a disconnected player from
        /// their party, so rejoin with the join code (or a new invite) after reconnecting.
        /// </summary>
        public PartyState Party { get; private set; }

        public event Action<MatchFoundMetadata> MatchFound;
        public event Action<ErrorData, bool> ErrorReceived;
        public event Action SearchSuperseded;
        public event Action<MatchmakingConnectionState> StateChanged;

        /// <summary>The server acknowledged the search (also raised for every member of a party).</summary>
        public event Action<SearchStartedData> SearchStarted;

        /// <summary>The party changed. Carries its full state; also available as <see cref="Party"/>.</summary>
        public event Action<PartyState> PartyUpdated;

        /// <summary>You left the party or were kicked from it.</summary>
        public event Action<PartyLeftData> PartyLeft;

        /// <summary>The party's search stopped before a match (see <see cref="PartySearchCancelledData.Reason"/>).</summary>
        public event Action<PartySearchCancelledData> PartySearchCancelled;

        /// <summary>Another player invited you to their party.</summary>
        public event Action<PartyInviteData> PartyInviteReceived;

        public MatchmakingClient(
            ITicketProvider ticketProvider,
            Func<IWebSocketTransport> transportFactory,
            MatchmakingClientOptions options = null)
        {
            _ticketProvider = ticketProvider ?? throw new ArgumentNullException(nameof(ticketProvider));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _options = options ?? new MatchmakingClientOptions();
        }

        /// <summary>
        /// Mints a fresh ticket, connects, and sends <c>begin_search</c>. If the same player has a
        /// search for this game mode on another connection, the server takes it over there (and
        /// that older connection gets <see cref="SearchSuperseded"/>) — this call always starts
        /// from a clean connection.
        /// </summary>
        /// <param name="gameMode">The game mode to search in.</param>
        /// <param name="preferredRegion">
        /// Optional region to lock the ticket to (see <see cref="ITicketProvider.GetTicketAsync"/>).
        /// Null means no preference. Every reconnect during this search reuses this same value, so
        /// a search that started region-locked stays locked to that region; PROTOCOL.md doesn't
        /// say what happens if a reconnect's fresh latency-routing pick differs from the original
        /// connection's region when no preference was given - this SDK doesn't try to detect or
        /// correct for that, it just repeats whatever selection criteria you originally gave it.
        /// </param>
        public async Task BeginSearchAsync(
            string gameMode, string preferredRegion = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(gameMode))
            {
                throw new ArgumentException("gameMode is required", nameof(gameMode));
            }

            _cancelRequested = false;
            _currentGameMode = gameMode;
            _reconnectAttempts = 0;

            // Connected ahead of time with ConnectAsync (to form a party, say): search on that
            // connection rather than replacing it, which would drop the party.
            if (_lobbyConnection && _transport != null && _transport.State == TransportState.Open)
            {
                _beginSearchPending = true;
                await SendAsync(new BeginSearchMessage(gameMode), cancellationToken).ConfigureAwait(false);

                if (State != MatchmakingConnectionState.Searching)
                {
                    EnterSearching();
                }

                return;
            }

            _currentPreferredRegion = preferredRegion;

            _lifetimeCts?.Cancel();
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            await ConnectAndSearchAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects without searching, so a party can be formed first: after this, use
        /// <see cref="CreatePartyAsync"/> / <see cref="JoinPartyAsync"/>, then
        /// <see cref="BeginSearchAsync"/> on the same connection (the party leader's call queues the
        /// whole party). Unlike a search, this connection is not reconnected if it drops: the server
        /// removes a disconnected player from their party, so there is nothing to resume. The state
        /// becomes <see cref="MatchmakingConnectionState.Closed"/>; call this again to start over.
        /// </summary>
        /// <param name="preferredRegion">
        /// Optional region to lock the ticket to. Everyone in a party must hold tickets for the
        /// same region, so pass the same value for the whole group.
        /// </param>
        /// <exception cref="Exception">
        /// Thrown when the ticket can't be fetched or the connection can't be opened.
        /// </exception>
        public async Task ConnectAsync(string preferredRegion = null, CancellationToken cancellationToken = default)
        {
            _cancelRequested = false;
            _currentGameMode = null;
            _currentPreferredRegion = preferredRegion;
            _reconnectAttempts = 0;
            _beginSearchPending = false;

            _lifetimeCts?.Cancel();
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = _lifetimeCts.Token;

            SetState(MatchmakingConnectionState.Connecting);
            DiscardTransport();
            Party = null;
            _lobbyConnection = false;

            try
            {
                IWebSocketTransport transport = await OpenTransportAsync(token).ConfigureAwait(false);
                _lobbyConnection = true;
                SetState(MatchmakingConnectionState.Connected);
                _ = ReceiveLoopAsync(transport, token);
            }
            catch (Exception)
            {
                SetState(MatchmakingConnectionState.Closed);
                throw;
            }
        }

        // --- Parties (PROTOCOL.md / asyncapi: create_party ... decline_invite). Each needs an open
        // connection (see ConnectAsync) and throws InvalidOperationException otherwise. The result
        // arrives as PartyUpdated / PartyLeft / ErrorReceived, not as a return value. ---

        /// <summary>Creates a party with you as leader; the <c>PARTY_UPDATE</c> carries the join code.</summary>
        public Task CreatePartyAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new CreatePartyMessage(), cancellationToken);

        /// <summary>Joins the party with this join code.</summary>
        public Task JoinPartyAsync(string code, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("code is required", nameof(code));
            }

            return SendAsync(new JoinPartyMessage(code), cancellationToken);
        }

        public Task LeavePartyAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new LeavePartyMessage(), cancellationToken);

        /// <summary>Leader only: removes a member.</summary>
        public Task KickMemberAsync(string userId, CancellationToken cancellationToken = default) =>
            SendAsync(new PartyUserMessage("kick_member", RequireUserId(userId)), cancellationToken);

        /// <summary>Leader only: hands leadership to another member.</summary>
        public Task PromoteLeaderAsync(string userId, CancellationToken cancellationToken = default) =>
            SendAsync(new PartyUserMessage("promote_leader", RequireUserId(userId)), cancellationToken);

        /// <summary>Leader only: invites a player by userId; they receive <c>PARTY_INVITE</c>.</summary>
        public Task InviteToPartyAsync(string userId, CancellationToken cancellationToken = default) =>
            SendAsync(new PartyUserMessage("invite_to_party", RequireUserId(userId)), cancellationToken);

        /// <summary>Accepts a <see cref="PartyInviteReceived"/> invite, using its <c>PartyId</c>.</summary>
        public Task AcceptInviteAsync(string partyId, CancellationToken cancellationToken = default) =>
            SendAsync(new PartyInviteReplyMessage("accept_invite", RequirePartyId(partyId)), cancellationToken);

        /// <summary>Optional: invites expire on their own after two minutes.</summary>
        public Task DeclineInviteAsync(string partyId, CancellationToken cancellationToken = default) =>
            SendAsync(new PartyInviteReplyMessage("decline_invite", RequirePartyId(partyId)), cancellationToken);

        private static string RequireUserId(string userId) =>
            string.IsNullOrEmpty(userId) ? throw new ArgumentException("userId is required", nameof(userId)) : userId;

        private static string RequirePartyId(string partyId) =>
            string.IsNullOrEmpty(partyId) ? throw new ArgumentException("partyId is required", nameof(partyId)) : partyId;

        public async Task CancelSearchAsync(CancellationToken cancellationToken = default)
        {
            _cancelRequested = true;
            StopHeartbeat();
            _beginSearchPending = false;

            IWebSocketTransport transport = _transport;
            if (transport != null && transport.State == TransportState.Open)
            {
                await SendAsync(new CancelSearchMessage(), cancellationToken).ConfigureAwait(false);
            }

            SetState(RestState());
        }

        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            _cancelRequested = true;
            _lifetimeCts?.Cancel();
            StopHeartbeat();
            _beginSearchPending = false;
            _lobbyConnection = false;
            Party = null;

            IWebSocketTransport transport = _transport;
            if (transport != null)
            {
                await transport.CloseAsync(cancellationToken).ConfigureAwait(false);
            }

            SetState(MatchmakingConnectionState.Closed);
        }

        private async Task ConnectAndSearchAsync(CancellationToken cancellationToken)
        {
            SetState(MatchmakingConnectionState.Connecting);

            // Every attempt gets a fresh connection, so whatever the previous attempt left behind
            // is finished with: the dropped socket on a reconnect, or the still-open one when
            // BeginSearchAsync is called again mid-search or after a match.
            DiscardTransport();
            _lobbyConnection = false;

            IWebSocketTransport transport = null;

            try
            {
                transport = await OpenTransportAsync(cancellationToken).ConfigureAwait(false);

                _beginSearchPending = true;
                await SendAsync(new BeginSearchMessage(_currentGameMode), cancellationToken).ConfigureAwait(false);
                SetState(MatchmakingConnectionState.Searching);
                StartHeartbeat();

                _ = ReceiveLoopAsync(transport, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {

                // Couldn't even get a connection up - e.g. the ticket backend is unreachable, or
                // the handshake itself failed. PROTOCOL.md section 6's ERROR codes all presuppose
                // a connection already exists, so without this the caller would see nothing at
                // all: BeginSearchAsync is normally fire-and-forget (see MatchmakingBehaviour),
                // and an unawaited faulted Task's exception just becomes unobserved. Surface it
                // through the same ErrorReceived event as any other error, then retry with the
                // same backoff as a dropped connection rather than duplicating that logic here.
                string json = JsonConvert.SerializeObject(new { code = ErrorCodes.ConnectionFailed, message = ex.Message });
                ErrorReceived?.Invoke(JsonConvert.DeserializeObject<ErrorData>(json), false);
                await HandleUnexpectedCloseAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // Mints a ticket, connects, and adopts the new transport as the current one. A transport
        // from an attempt that fails before being adopted is disposed here.
        private async Task<IWebSocketTransport> OpenTransportAsync(CancellationToken cancellationToken)
        {
            IWebSocketTransport transport = null;

            try
            {
                TicketInfo ticket = await _ticketProvider
                    .GetTicketAsync(_currentPreferredRegion, cancellationToken)
                    .ConfigureAwait(false);
                transport = _transportFactory();

                var uri = new Uri($"{ticket.WsEndpoint.TrimEnd('/')}/?ticket={Uri.EscapeDataString(ticket.Ticket)}");
                await transport.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

                _transport = transport;
                return transport;
            }
            catch (Exception)
            {
                DisposeIfNeverAdopted(transport);
                throw;
            }
        }

        private async Task ReceiveLoopAsync(IWebSocketTransport transport, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsCurrent(transport))
                {
                    TransportReceiveResult result = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);

                    // This transport was replaced (a new search or reconnect started) while we were
                    // waiting: anything it reports now is about a connection nobody is using.
                    if (!IsCurrent(transport))
                    {
                        return;
                    }

                    if (result.IsClose)
                    {
                        await HandleUnexpectedCloseAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await HandleMessageAsync(result.Message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on deliberate shutdown.
            }
            catch (Exception)
            {
                // A fault on a transport we've already replaced (e.g. it was disposed under us) is
                // expected and must not disturb the connection that replaced it.
                if (!IsCurrent(transport))
                {
                    return;
                }

                // Unexpected transport fault: treat like a dropped connection.
                await HandleUnexpectedCloseAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleMessageAsync(string json, CancellationToken cancellationToken)
        {
            ServerMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<ServerMessage>(json, ServerMessageSettings);
            }
            catch (JsonException)
            {
                // Malformed frame from the server: not a documented protocol case, ignore defensively.
                return;
            }

            switch (message)
            {
                case null:
                case UnknownServerMessage _:
                    // Forward compatibility: PROTOCOL.md's versioning rules allow new messages to
                    // be added in additive releases. Ignore anything we don't recognise.
                    break;

                case MatchFoundMessage matchFound:
                    // The search is finished. Without this, the connection closing later (an idle
                    // timeout, say) would look like an unexpected drop and reconnect, sending
                    // begin_search again and queueing a player who was just matched.
                    _cancelRequested = true;
                    _beginSearchPending = false;
                    StopHeartbeat();
                    SetState(RestState());
                    MatchFound?.Invoke(matchFound.Data?.Metadata);
                    break;

                case PingMessage ping:
                    // A party member never called BeginSearchAsync, so the game mode comes from the
                    // party's own state (sent while it is STARTING).
                    await SendAsync(
                            new PongMessage(ping.Data?.CorrelationId, Party?.GameMode ?? _currentGameMode),
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case SearchStartedMessage started:
                    _beginSearchPending = false;

                    // Only a party's leader called BeginSearchAsync; the other members find out here
                    // and must heartbeat from now on.
                    if (State != MatchmakingConnectionState.Searching)
                    {
                        EnterSearching();
                    }

                    SearchStarted?.Invoke(started.Data);
                    break;

                case PartyUpdateMessage update:
                    HandlePartyUpdate(update.Data?.Party);
                    break;

                case PartyLeftMessage left:
                    Party = null;
                    LeaveSearching();
                    PartyLeft?.Invoke(left.Data);
                    break;

                case PartySearchCancelledMessage cancelled:
                    LeaveSearching();
                    PartySearchCancelled?.Invoke(cancelled.Data);
                    break;

                case PartyInviteMessage invite:
                    PartyInviteReceived?.Invoke(invite.Data);
                    break;

                case SearchSupersededMessage _:
                    // A newer connection took over the search; this one is done. The server closes
                    // it, so don't auto-reconnect.
                    _cancelRequested = true;
                    StopHeartbeat();
                    SetState(MatchmakingConnectionState.Closed);
                    SearchSuperseded?.Invoke();
                    break;

                case CloseConnectionMessage _:
                    // PROTOCOL.md doesn't distinguish a server-initiated CLOSE_CONNECTION from an
                    // unexpected drop, so this SDK treats them the same: reconnect if still
                    // searching. This is a judgment call, not an explicit spec requirement.
                    await HandleUnexpectedCloseAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case ErrorMessage error:
                    bool terminal = ErrorCodes.IsTerminal(error.Data?.Code);
                    ErrorReceived?.Invoke(error.Data, terminal);

                    if (terminal)
                    {
                        _cancelRequested = true;
                        StopHeartbeat();
                        SetState(MatchmakingConnectionState.Faulted);
                    }
                    else if (error.Data?.Code == ErrorCodes.StalePong && State == MatchmakingConnectionState.Searching)
                    {
                        // PROTOCOL.md section 6: a stale pong should retry begin_search.
                        await SendAsync(new BeginSearchMessage(_currentGameMode), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (_beginSearchPending && IsSearchRejection(error.Data?.Code))
                    {
                        // The server refused (or rolled back) the search, so nothing is queued.
                        LeaveSearching();
                    }
                    break;
            }
        }

        private static bool IsSearchRejection(string code)
        {
            switch (code)
            {
                case ErrorCodes.SearchStartFailed:
                case ErrorCodes.NotPartyLeader:
                case ErrorCodes.PartiesNotEnabled:
                case ErrorCodes.PartyTooLarge:
                case ErrorCodes.PartyMemberAttributeMissing:
                case ErrorCodes.PartyChanged:
                    return true;
                default:
                    return false;
            }
        }

        private void HandlePartyUpdate(PartyState party)
        {
            if (party == null)
            {
                return;
            }

            bool wasInParty = Party != null;
            PartyStatus previous = Party?.PartyStatus ?? PartyStatus.Lobby;
            Party = party;

            // The party is back in the lobby without a MATCH_FOUND (a ping check that expired, or
            // joining/creating a party cancelled a search of our own): we are no longer searching.
            // Only on a real change, so a stale lobby update can't undo a search that has just begun.
            if (party.PartyStatus == PartyStatus.Lobby
                && State == MatchmakingConnectionState.Searching
                && (!wasInParty || previous != PartyStatus.Lobby))
            {
                LeaveSearching();
            }

            PartyUpdated?.Invoke(party);
        }

        private void EnterSearching()
        {
            SetState(MatchmakingConnectionState.Searching);
            StartHeartbeat();
        }

        // Back to the resting state after a search that didn't produce a match.
        private void LeaveSearching()
        {
            _beginSearchPending = false;

            if (State == MatchmakingConnectionState.Searching)
            {
                StopHeartbeat();
                SetState(RestState());
            }
        }

        private MatchmakingConnectionState RestState() =>
            _lobbyConnection ? MatchmakingConnectionState.Connected : MatchmakingConnectionState.Idle;

        private async Task HandleUnexpectedCloseAsync(CancellationToken cancellationToken)
        {
            StopHeartbeat();

            // Don't stomp on a more specific terminal state (e.g. Faulted from a terminal ERROR)
            // that already explains why we're not reconnecting - the socket closing afterwards is
            // just a consequence of that, not new information.
            void CloseUnlessAlreadyFaulted()
            {
                if (State != MatchmakingConnectionState.Faulted)
                {
                    SetState(MatchmakingConnectionState.Closed);
                }
            }

            // A party doesn't survive a dropped connection (the server removes the player), and a
            // connection that wasn't searching has nothing to resume: don't reconnect either.
            if (Party != null || (_lobbyConnection && State != MatchmakingConnectionState.Searching))
            {
                Party = null;
                _lobbyConnection = false;
                _beginSearchPending = false;
                CloseUnlessAlreadyFaulted();
                return;
            }

            if (_cancelRequested || cancellationToken.IsCancellationRequested)
            {
                CloseUnlessAlreadyFaulted();
                return;
            }

            if (_options.MaxReconnectAttempts.HasValue && _reconnectAttempts >= _options.MaxReconnectAttempts.Value)
            {
                CloseUnlessAlreadyFaulted();
                return;
            }

            SetState(MatchmakingConnectionState.Reconnecting);
            _reconnectAttempts++;

            TimeSpan delay = ComputeBackoff(_reconnectAttempts);

            try
            {
                await _options.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CloseUnlessAlreadyFaulted();
                return;
            }

            if (_cancelRequested || cancellationToken.IsCancellationRequested)
            {
                CloseUnlessAlreadyFaulted();
                return;
            }

            try
            {
                // PROTOCOL.md section 7: reconnect with a fresh ticket, resend begin_search for the
                // same game mode. Within ~30s of the drop, the server resumes the held search.
                await ConnectAndSearchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                SetState(MatchmakingConnectionState.Closed);
            }
        }

        private TimeSpan ComputeBackoff(int attempt)
        {
            double seconds = _options.InitialReconnectDelay.TotalSeconds * Math.Pow(2, attempt - 1);
            seconds = Math.Min(seconds, _options.MaxReconnectDelay.TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatCts = new CancellationTokenSource();
            _ = HeartbeatLoopAsync(_heartbeatCts.Token);
        }

        private void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = null;
        }

        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await _options.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (State == MatchmakingConnectionState.Searching)
                    {
                        await SendAsync(new HeartbeatMessage(), cancellationToken).ConfigureAwait(false);

                        // The connection has stayed up for a whole heartbeat interval and accepted
                        // a send: it's stable, so a later drop starts backoff from scratch and
                        // doesn't count against MaxReconnectAttempts. A connection that is accepted
                        // and then dropped straight away never gets here, so it keeps backing off.
                        _reconnectAttempts = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the search ends.
            }
            catch (Exception)
            {
                // The transport failed or was disposed under us. The receive loop notices a real
                // drop and handles it; an unobserved exception here would only be noise.
            }
        }

        private Task SendAsync(object message, CancellationToken cancellationToken)
        {
            IWebSocketTransport transport = _transport;
            if (transport == null)
            {
                throw new InvalidOperationException("There is no open connection to send on.");
            }

            string json = JsonConvert.SerializeObject(message);
            return transport.SendAsync(json, cancellationToken);
        }

        private bool IsCurrent(IWebSocketTransport transport) => ReferenceEquals(transport, _transport);

        private void DiscardTransport()
        {
            StopHeartbeat();

            IWebSocketTransport previous = _transport;
            _transport = null;
            previous?.Dispose();
        }

        // A transport created for an attempt that failed before it was adopted as the current one
        // would otherwise never be disposed.
        private void DisposeIfNeverAdopted(IWebSocketTransport transport)
        {
            if (transport != null && !IsCurrent(transport))
            {
                transport.Dispose();
            }
        }

        private void SetState(MatchmakingConnectionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose()
        {
            _lifetimeCts?.Cancel();
            StopHeartbeat();
            _transport?.Dispose();
        }
    }
}
