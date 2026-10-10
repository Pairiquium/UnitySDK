using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Result of a successful <see cref="MatchConnectionClient.WaitForConnectionAsync"/> call:
    /// where to connect for gameplay, once the match webhook has reached the backend.
    /// </summary>
    internal sealed class MatchConnectionInfo
    {
        public string RelayWsUrl { get; }
        public long Team { get; }
        public string GameMode { get; }

        public MatchConnectionInfo(string relayWsUrl, long team, string gameMode)
        {
            RelayWsUrl = relayWsUrl;
            Team = team;
            GameMode = gameMode;
        }
    }

    /// <summary>
    /// Long-polls this sample's own backend (<c>Backend~/arena-backend.js</c>) to learn the
    /// relay WebSocket address for a match, once Pairiquium's real match webhook has reached it.
    ///
    /// This is NOT part of the Pairiquium protocol - <c>MATCH_FOUND</c> only ever carries
    /// matchId+team (see <see cref="Pairiquium.Messages.MatchFoundMetadata"/>), by design
    /// (PROTOCOL.md section 4: getting players from "matched" to a game server is "your own
    /// channel"). This class is that channel's client side, specific to this sample's own
    /// invented <c>/matches/{matchId}/connection</c> contract - see Backend~'s README.md.
    /// </summary>
    internal sealed class MatchConnectionClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _backendBaseUrl;

        /// <param name="httpClient">Caller-owned; this client does not dispose it.</param>
        /// <param name="backendBaseUrl">e.g. "http://localhost:8787" - no trailing slash needed.</param>
        public MatchConnectionClient(HttpClient httpClient, string backendBaseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrEmpty(backendBaseUrl))
            {
                throw new ArgumentException("backendBaseUrl is required", nameof(backendBaseUrl));
            }
            _backendBaseUrl = backendBaseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Polls until the backend reports a relay address for this match, or cancellation is
        /// requested. The backend holds each request open for a while (a long-poll, not a
        /// short-poll or a push) before responding <c>{ ready: false }</c> - this loops that
        /// into waiting indefinitely rather than giving up after one cycle, since a real webhook
        /// retry (e.g. a cold-starting tunnel) can land well after any single poll's timeout.
        /// </summary>
        public async Task<MatchConnectionInfo> WaitForConnectionAsync(
            string matchId, long team, CancellationToken cancellationToken)
        {
            string url = $"{_backendBaseUrl}/matches/{Uri.EscapeDataString(matchId)}/connection?team={team}";

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (HttpResponseMessage response = await _httpClient
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    // netstandard2.1's HttpContent.ReadAsStringAsync has no CancellationToken overload.
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var parsed = JsonConvert.DeserializeObject<ConnectionResponseModel>(body);

                    if (parsed != null && parsed.Ready && !string.IsNullOrEmpty(parsed.RelayWsUrl))
                    {
                        return new MatchConnectionInfo(parsed.RelayWsUrl, parsed.Team, parsed.GameMode);
                    }
                }

                // ready: false - the backend's own long-poll timeout elapsed first with no
                // webhook yet. Re-issue immediately; the backend already did the waiting.
            }
        }

        private sealed class ConnectionResponseModel
        {
            [JsonProperty("ready")]
            public bool Ready { get; set; }

            [JsonProperty("relayWsUrl")]
            public string RelayWsUrl { get; set; }

            [JsonProperty("team")]
            public long Team { get; set; }

            [JsonProperty("gameMode")]
            public string GameMode { get; set; }
        }
    }
}
