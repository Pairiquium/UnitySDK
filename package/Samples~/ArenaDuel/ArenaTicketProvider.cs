using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pairiquium.Tickets;

namespace Pairiquium.Samples
{
    /// <summary>
    /// Same job as <see cref="HttpTicketProvider"/> (POST to YOUR backend's ticket endpoint), plus
    /// one thing the stock provider doesn't surface: the <c>userId</c> this sample's backend echoes
    /// back with the ticket. Parties need it. <c>PARTY_UPDATE</c> names the leader by userId, so a
    /// client can only tell whether it is the leader by knowing its own, and
    /// <c>invite_to_party</c> takes another player's userId.
    ///
    /// Pairiquium itself only ever hears the userId your backend chose when it minted the ticket,
    /// so a real game already knows it (it's the player's account id); it doesn't need to come back
    /// through the ticket response at all. This sample's backend invents a random id per ticket,
    /// which is why it has to be echoed. A new id per connection also means a reconnecting player
    /// is a new player as far as the sample is concerned.
    /// </summary>
    internal sealed class ArenaTicketProvider : ITicketProvider
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _endpoint;

        /// <summary>The userId of the most recently minted ticket, or null before the first.</summary>
        public string UserId { get; private set; }

        public ArenaTicketProvider(HttpClient httpClient, Uri endpoint)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public async Task<TicketInfo> GetTicketAsync(string preferredRegion, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, _endpoint))
            {
                if (!string.IsNullOrEmpty(preferredRegion))
                {
                    string json = JsonConvert.SerializeObject(new { preferredRegion });
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using (HttpResponseMessage response = await _httpClient
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var parsed = JsonConvert.DeserializeObject<Response>(body);

                    if (string.IsNullOrEmpty(parsed?.Ticket) || string.IsNullOrEmpty(parsed?.WsEndpoint))
                    {
                        throw new InvalidOperationException(
                            "Ticket endpoint response did not contain both 'ticket' and 'wsEndpoint'.");
                    }

                    UserId = parsed.UserId;
                    return new TicketInfo(parsed.Ticket, parsed.WsEndpoint);
                }
            }
        }

        private sealed class Response
        {
            [JsonProperty("ticket")] public string Ticket { get; set; }
            [JsonProperty("wsEndpoint")] public string WsEndpoint { get; set; }
            [JsonProperty("userId")] public string UserId { get; set; }
        }
    }
}
