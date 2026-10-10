using System;
using System.Net.Http;
using Pairiquium;
using Pairiquium.Messages;
using Pairiquium.Tickets;
using Pairiquium.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Fuller end-to-end example than a single hardcoded search: lets the player pick a game mode
    /// and a region (or "Automatic" for latency routing) with button groups, start/cancel a search,
    /// and see connection state, match-found, and error feedback as separate panels.
    ///
    /// Wires a MatchmakingBehaviour to an HttpTicketProvider pointed at YOUR OWN backend's
    /// ticket-issuing endpoint (never Pairiquium directly - see HttpTicketProvider's docs).
    ///
    /// This is a starting point to copy into your own project, not a drop-in final solution:
    /// you still need a backend endpoint that calls Pairiquium's POST /v1/tickets with your
    /// secret API key and forwards { ticket, wsEndpoint } back to the client.
    ///
    /// The region list below is just a few illustrative examples (see asyncapi.json's `region`
    /// variable examples) - API-Specs doesn't expose a "list active regions" endpoint, so a real
    /// game would get this list from wherever it already tracks its own deployment regions.
    /// </summary>
    public sealed class MatchmakingSample : MonoBehaviour
    {
        private static readonly Color SelectedColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color UnselectedColor = Color.white;
        private static readonly Color ErrorColor = new Color(0.9f, 0.3f, 0.3f);
        private static readonly Color OkColor = new Color(0.3f, 0.8f, 0.4f);
        private static readonly Color NeutralColor = Color.black;

        [SerializeField]
        [Tooltip("Your own backend's ticket-issuing endpoint. Never point this at Pairiquium directly.")]
        private string ticketEndpointUrl = "https://your-backend.example.com/matchmaking/ticket";

        [Header("Status")]
        [SerializeField] private Text statusText;

        [Header("Game mode selection (click to select, highlighted = selected)")]
        [SerializeField] private Image rankedModeHighlight;
        [SerializeField] private Image fastModeHighlight;
        [SerializeField] private Image qualityModeHighlight;

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

        private const string RankedGameMode = "RANKED_1V1";
        private const string FastGameMode = "FIFO";
        private const string QualityGameMode = "PING";

        // Empty string (not null) means "Automatic" was picked explicitly; converted to null -
        // i.e. no preference - right before calling BeginSearchAsync.
        private const string AutomaticRegion = "";
        private const string UsEastRegion = "us-east-1";
        private const string EuWestRegion = "eu-west-2";

        private MatchmakingBehaviour _matchmaking;
        private HttpClient _httpClient;
        private string _selectedGameMode = FastGameMode;
        private string _selectedRegion = AutomaticRegion;

        private void Start()
        {
            _httpClient = new HttpClient();
            var ticketProvider = new HttpTicketProvider(_httpClient, new System.Uri(ticketEndpointUrl));

            _matchmaking = gameObject.AddComponent<MatchmakingBehaviour>();
            _matchmaking.Initialize(ticketProvider);

            _matchmaking.OnStateChanged.AddListener(OnStateChanged);
            _matchmaking.OnMatchFound.AddListener(OnMatchFound);
            _matchmaking.OnError.AddListener(OnError);
            _matchmaking.OnSearchSuperseded.AddListener(OnSearchSuperseded);

            RefreshGameModeHighlights();
            RefreshRegionHighlights();
            HideFeedbackPanels();
            SetStatus("Idle", NeutralColor);
            SetSearchControlsInteractable(true);
        }

        // Wired to the game mode buttons' OnClick() in the sample scene.
        public void SelectRankedMode() => SelectGameMode(RankedGameMode);
        public void SelectFastMode() => SelectGameMode(FastGameMode);
        public void SelectQualityMode() => SelectGameMode(QualityGameMode);

        // Wired to the region buttons' OnClick() in the sample scene.
        public void SelectAutomaticRegion() => SelectRegion(AutomaticRegion);
        public void SelectUsEastRegion() => SelectRegion(UsEastRegion);
        public void SelectEuWestRegion() => SelectRegion(EuWestRegion);

        private void SelectGameMode(string gameMode)
        {
            _selectedGameMode = gameMode;
            RefreshGameModeHighlights();
        }

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
                await _matchmaking.BeginSearchAsync(_selectedGameMode, region);
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

        // Wired to the match-found and error panels' own close buttons.
        public void CloseMatchFoundPanel() => matchFoundPanel.SetActive(false);
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

            // Only one search can be in flight at a time - keep the buttons honest about that
            // instead of letting a second click stack a redundant begin_search.
            bool searching = state == MatchmakingConnectionState.Searching
                || state == MatchmakingConnectionState.Connecting
                || state == MatchmakingConnectionState.Reconnecting;
            SetSearchControlsInteractable(!searching);
        }

        private void OnMatchFound(MatchFoundMetadata metadata)
        {
            Debug.Log($"[Pairiquium] match found: matchId={metadata.MatchId} team={metadata.Team}");
            errorPanel.SetActive(false);
            matchFoundText.text = $"Match found!\nMatch ID: {metadata.MatchId}\nTeam: {metadata.Team}";
            matchFoundPanel.SetActive(true);
            // Your backend gets the webhook for this same matchId and is responsible for getting
            // you connection details for the game server - MATCH_FOUND doesn't carry those.
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

            string prefix = isTerminal ? "Error (search stopped)" : "Error (still searching)";
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
                    Debug.LogError($"'{_selectedGameMode}' isn't available right now.");
                    break;
            }
        }

        private void OnSearchSuperseded()
        {
            Debug.Log("[Pairiquium] search superseded by a newer connection for this player.");
            SetStatus("Superseded", SelectedColor);
        }

        private void RefreshGameModeHighlights()
        {
            rankedModeHighlight.color = _selectedGameMode == RankedGameMode ? SelectedColor : UnselectedColor;
            fastModeHighlight.color = _selectedGameMode == FastGameMode ? SelectedColor : UnselectedColor;
            qualityModeHighlight.color = _selectedGameMode == QualityGameMode ? SelectedColor : UnselectedColor;
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

        private void SetSearchControlsInteractable(bool canSearch)
        {
            searchButton.interactable = canSearch;
            cancelButton.interactable = !canSearch;
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
            _httpClient?.Dispose();
        }
    }
}
