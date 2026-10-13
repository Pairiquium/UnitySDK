using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pairiquium;
using Pairiquium.Messages;
using Pairiquium.Tickets;
using Pairiquium.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Pairiquium.Samples
{
    /// <summary>
    /// The matchmaking leg only: region selection, search/cancel, match-found/error feedback -
    /// exactly the same shape as BasicMatchmaking.cs, trimmed to one fixed game mode since this
    /// sample only plays one. This class knows nothing about the arena, the relay, or gameplay;
    /// it hands off to <see cref="ArenaGameController"/> for everything past "where do I
    /// connect for gameplay" and gets control back via that class's <c>OnReturnedToLobby</c>
    /// event - the same two-event shape <c>MatchmakingBehaviour</c> itself uses (you listen for
    /// what you need, you don't reach into the other side's internals).
    ///
    /// The handoff itself happens in <see cref="OnMatchFound"/>: once MATCH_FOUND arrives, this
    /// class long-polls Backend~'s own /matches/{matchId}/connection endpoint (via
    /// MatchConnectionClient) until the real Pairiquium match webhook has reached that backend
    /// and a relay address comes back, then calls <see cref="ArenaGameController.EnterArenaAsync"/>
    /// with it. That one call is the entire seam between "matchmaking" and "the game" - read
    /// ArenaGameController next for what happens on the other side of it.
    ///
    /// Before any of that, <see cref="ArenaUsernamePanel"/> gates the whole lobby behind a
    /// "choose a username" step - the id a party invite needs is otherwise a random string
    /// nobody can read and share. Parties themselves are handled by <see cref="ArenaPartyPanel"/>,
    /// which this class creates under the lobby canvas; this class only asks it whether the local
    /// player may start a search (in a party, only the leader may).
    ///
    /// Wires a MatchmakingBehaviour to an ArenaTicketProvider (an HttpTicketProvider that also keeps
    /// the userId) pointed at YOUR OWN backend's
    /// ticket-issuing endpoint (never Pairiquium directly - see HttpTicketProvider's docs). The
    /// Backend~/ folder next to this script is a complete, runnable reference for that backend,
    /// covering ticket issuing AND the match webhook AND the gameplay relay - see its README.md.
    /// </summary>
    public sealed class ArenaDuelSample : MonoBehaviour
    {
        private static readonly Color SelectedColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color UnselectedColor = Color.white;
        private static readonly Color ErrorColor = new Color(0.9f, 0.3f, 0.3f);
        private static readonly Color OkColor = new Color(0.3f, 0.8f, 0.4f);
        private static readonly Color NeutralColor = Color.black;

        // This sample plays exactly one game mode - unlike BasicMatchmaking, there's no mode
        // picker. Must match a game mode actually configured on your Pairiquium account, with
        // its NotificationWebhook pointed at this sample's backend (see Backend~/README.md).
        private const string GameMode = "ARENA_1V1";

        // Empty string (not null) means "Automatic" was picked explicitly; converted to null -
        // i.e. no preference - right before calling BeginSearchAsync.
        private const string AutomaticRegion = "";
        private const string UsEastRegion = "us-east-1";
        private const string EuWestRegion = "eu-west-2";

        [SerializeField]
        [Tooltip("Your own backend's ticket-issuing endpoint. Never point this at Pairiquium directly. Point this at this sample's own Backend~ reference server's /matchmaking/ticket route.")]
        private string ticketEndpointUrl = "http://localhost:8787/matchmaking/ticket";

        [SerializeField]
        [Tooltip("Base URL of this sample's own backend (Backend~/arena-backend.js), used to ask where to connect for gameplay after a match is found. No trailing slash.")]
        private string backendBaseUrl = "http://localhost:8787";

        [Header("Status")]
        [SerializeField] private Text statusText;

        [Header("Region selection (click to select, highlighted = selected)")]
        [SerializeField] private Image automaticRegionHighlight;
        [SerializeField] private Image usEastRegionHighlight;
        [SerializeField] private Image euWestRegionHighlight;

        [Header("Actions")]
        [SerializeField] private Button searchButton;
        [SerializeField] private Button cancelButton;

        [Header("Feedback panels")]
        [SerializeField] private GameObject matchFoundPanel;
        [SerializeField] private Text matchFoundText;
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private Text errorText;

        [Header("Handoff")]
        [Tooltip("Everything past 'where do I connect for gameplay' - this class calls EnterArenaAsync on it once a relay address is ready, and listens for OnReturnedToLobby to know when to show lobby UI again.")]
        [SerializeField] private ArenaGameController gameController;

        private MatchmakingBehaviour _matchmaking;
        private ArenaPartyPanel _partyPanel;
        private MatchmakingConnectionState _state = MatchmakingConnectionState.Idle;
        private MatchConnectionClient _connectionClient;
        private CancellationTokenSource _connectionWaitCts;
        private HttpClient _httpClient;
        private string _selectedRegion = AutomaticRegion;

        private void Start()
        {
            _httpClient = new HttpClient();
            var ticketProvider = new ArenaTicketProvider(_httpClient, new System.Uri(ticketEndpointUrl));
            _connectionClient = new MatchConnectionClient(_httpClient, backendBaseUrl);

            _matchmaking = gameObject.AddComponent<MatchmakingBehaviour>();
            _matchmaking.Initialize(ticketProvider);

            _matchmaking.OnStateChanged.AddListener(OnStateChanged);
            _matchmaking.OnMatchFound.AddListener(OnMatchFound);
            _matchmaking.OnError.AddListener(OnError);
            _matchmaking.OnSearchSuperseded.AddListener(OnSearchSuperseded);

            gameController.OnReturnedToLobby -= HandleReturnedToLobby;
            gameController.OnReturnedToLobby += HandleReturnedToLobby;

            // Parties are optional and need the game mode to have them enabled (see Backend~/README.md).
            // Created under the lobby canvas, so it hides with the lobby when the arena opens.
            _partyPanel = ArenaPartyPanel.Create(
                searchButton.GetComponentInParent<Canvas>().transform,
                _matchmaking,
                ticketProvider,
                () => string.IsNullOrEmpty(_selectedRegion) ? null : _selectedRegion);
            _partyPanel.Changed += RefreshSearchControls;

            // Created last so it's the topmost sibling under the canvas: its full-screen
            // background blocks clicks to everything above (region/search/party) until the
            // player picks a username, which ArenaTicketProvider then uses as its userId - see
            // ArenaUsernamePanel's doc comment for why that's needed for party invites.
            ArenaUsernamePanel.Create(
                searchButton.GetComponentInParent<Canvas>().transform, ticketProvider, RefreshSearchControls);

            RefreshRegionHighlights();
            HideFeedbackPanels();
            SetStatus("Idle", NeutralColor);
            RefreshSearchControls();
        }

        // Wired to the region buttons' OnClick() in the sample scene.
        public void SelectAutomaticRegion() => SelectRegion(AutomaticRegion);
        public void SelectUsEastRegion() => SelectRegion(UsEastRegion);
        public void SelectEuWestRegion() => SelectRegion(EuWestRegion);

        private void SelectRegion(string region)
        {
            _selectedRegion = region;
            RefreshRegionHighlights();
        }

        // Wired to the Search button's OnClick(). async void (not "_ = ...BeginSearchAsync()") is
        // deliberate: this IS the top-level UI event handler Unity's Button.onClick calls
        // directly, so this try/catch is the only place anything could ever observe a failure
        // here. MatchmakingClient already reports connection failures through OnError below, but
        // discarding the Task with "_ = ..." would silently eat any *other* unexpected exception
        // too - don't copy that pattern into your own game without this try/catch around it.
        public async void BeginSearch()
        {
            HideFeedbackPanels();
            string region = string.IsNullOrEmpty(_selectedRegion) ? null : _selectedRegion;
            try
            {
                await _matchmaking.BeginSearchAsync(GameMode, region);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowError($"Unexpected error starting search: {ex.Message}");
            }
        }

        // Wired to the Cancel button's OnClick(). See BeginSearch() above for why async void.
        public async void CancelSearch()
        {
            try
            {
                await _matchmaking.CancelSearchAsync();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowError($"Unexpected error cancelling search: {ex.Message}");
            }
        }

        // Wired to the match-found panel's own close button. Also doubles as "stop waiting for
        // a game server" if the player closes the panel before the connection handoff below
        // finishes - without this, that wait would silently keep polling forever in the background.
        public void CloseMatchFoundPanel()
        {
            _connectionWaitCts?.Cancel();
            matchFoundPanel.SetActive(false);
        }

        public void DismissErrorPanel() => errorPanel.SetActive(false);

        private void OnStateChanged(MatchmakingConnectionState state)
        {
            Debug.Log($"[Pairiquium] state -> {state}");

            Color color = NeutralColor;
            if (state == MatchmakingConnectionState.Searching)
            {
                color = OkColor;
            }
            else if (state == MatchmakingConnectionState.Faulted)
            {
                color = ErrorColor;
            }
            else if (state == MatchmakingConnectionState.Reconnecting)
            {
                color = SelectedColor;
            }
            SetStatus(state.ToString(), color);
            _state = state;
            RefreshSearchControls();
        }

        // Only one search can be in flight at a time - keep the buttons honest about that instead
        // of letting a second click stack a redundant begin_search. In a party only the leader
        // searches or cancels; the other members just see the status change when it starts.
        private void RefreshSearchControls()
        {
            bool searching = _state == MatchmakingConnectionState.Searching
                || _state == MatchmakingConnectionState.Connecting
                || _state == MatchmakingConnectionState.Reconnecting;
            bool mayControl = _partyPanel == null || !_partyPanel.InParty || _partyPanel.IsLeader;

            searchButton.interactable = !searching && mayControl;
            cancelButton.interactable = searching && mayControl;
        }

        // THE HANDOFF: matchmaking ends here and the game begins. async void here is the same
        // deliberate choice as BeginSearch()/CancelSearch() above - this is a UnityEvent
        // listener (added via OnMatchFound.AddListener), so nothing else can ever await it, and
        // this try/catch is the only place a failure anywhere in the handoff (the long-poll OR
        // the relay connect inside EnterArenaAsync) is observable.
        private async void OnMatchFound(MatchFoundMetadata metadata)
        {
            Debug.Log($"[Pairiquium] match found: matchId={metadata.MatchId} team={metadata.Team}");
            errorPanel.SetActive(false);
            matchFoundText.text = $"Match found! Team {metadata.Team}.\nConnecting to your game server...";
            matchFoundPanel.SetActive(true);

            // A newer match (e.g. after a cancel + re-search) should supersede any wait still in
            // flight for a previous one, not race it.
            _connectionWaitCts?.Cancel();
            _connectionWaitCts = new CancellationTokenSource();

            try
            {
                MatchConnectionInfo connection = await _connectionClient
                    .WaitForConnectionAsync(metadata.MatchId, metadata.Team, _connectionWaitCts.Token);

                Debug.Log($"[ArenaDuel] relay ready: {connection.RelayWsUrl}");
                matchFoundPanel.SetActive(false);
                await gameController.EnterArenaAsync(connection.RelayWsUrl);
            }
            catch (OperationCanceledException)
            {
                // Expected: the player closed the panel, or a newer match superseded this wait.
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowError($"Unexpected error connecting to your game server: {ex.Message}");
            }
        }

        // Wired to ArenaGameController.OnReturnedToLobby - the reverse handoff, fired once the
        // player clicks "Back to Lobby". ArenaGameController has already hidden the arena and
        // shown lobbyCanvas itself by this point; this only resets the matchmaking-specific UI
        // it owns.
        private void HandleReturnedToLobby()
        {
            HideFeedbackPanels();
            // Idle, or Connected if a party kept the matchmaking connection open.
            SetStatus(_matchmaking.State.ToString(), NeutralColor);
            RefreshSearchControls();
        }

        private void OnError(ErrorData error, bool isTerminal)
        {
            string details = "none";
            if (error.Details != null && error.Details.Count > 0)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kv in error.Details)
                {
                    parts.Add($"{kv.Key}={kv.Value}");
                }
                details = string.Join(", ", parts);
            }

            Debug.LogWarning($"[Pairiquium] error {error.Code} (terminal={isTerminal}): {error.Message} (details: {details})");

            // Not "still searching": party errors (NOT_PARTY_LEADER, PARTY_TOO_LARGE, ...) can arrive
            // while nothing is searching.
            string prefix = isTerminal ? "Error (search stopped)" : "Error";
            ShowError($"{prefix}\n{error.Code}: {error.Message}");

            // error.ErrorCode is the typed form of error.Code (a plain string) - handle it with a
            // switch instead of string comparisons. PairiquiumErrorCode.Unknown covers any code
            // this SDK version doesn't recognise yet; fall back to error.Code for those.
            switch (error.ErrorCode)
            {
                case PairiquiumErrorCode.PlayerAttributeRequired:
                    Debug.LogError("Ticket is missing a required attribute - check attrs when minting it.");
                    break;
                case PairiquiumErrorCode.GameModeNotFound:
                case PairiquiumErrorCode.GameModeDisabled:
                    Debug.LogError($"'{GameMode}' isn't available right now - check it's configured on your Pairiquium account with its NotificationWebhook pointed at this sample's backend.");
                    break;
            }
        }

        private void OnSearchSuperseded()
        {
            Debug.Log("[Pairiquium] search superseded by a newer connection for this player.");
            SetStatus("Superseded", SelectedColor);
        }

        private void RefreshRegionHighlights()
        {
            automaticRegionHighlight.color = _selectedRegion == AutomaticRegion ? SelectedColor : UnselectedColor;
            usEastRegionHighlight.color = _selectedRegion == UsEastRegion ? SelectedColor : UnselectedColor;
            euWestRegionHighlight.color = _selectedRegion == EuWestRegion ? SelectedColor : UnselectedColor;
        }

        private void HideFeedbackPanels()
        {
            matchFoundPanel.SetActive(false);
            errorPanel.SetActive(false);
        }

        private void ShowError(string text)
        {
            matchFoundPanel.SetActive(false);
            errorText.text = text;
            errorPanel.SetActive(true);
        }

        private void SetStatus(string text, Color color)
        {
            if (statusText != null)
            {
                statusText.text = text;
                statusText.color = color;
            }
        }

        private void OnDestroy()
        {
            _connectionWaitCts?.Cancel();
            _httpClient?.Dispose();
            if (gameController != null)
            {
                gameController.OnReturnedToLobby -= HandleReturnedToLobby;
            }
        }
    }
}
