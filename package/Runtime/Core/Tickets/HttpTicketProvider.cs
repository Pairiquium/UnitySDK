using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Pairiquium.Tickets
{
    /// <summary>
    /// Posts to <b>your own backend's</b> ticket-issuing endpoint — never to Pairiquium directly,
    /// since the SDK must never hold the Pairiquium API key (PROTOCOL.md section 1). Your backend
    /// is the one that calls Pairiquium's <c>POST /v1/tickets</c> with your secret key and forwards
    /// the result.
    ///
    /// The request/response contract with YOUR backend is not something API-Specs defines (it only
    /// covers the Pairiquium-facing side of that call). This provider assumes your endpoint takes a
    /// POST and responds with JSON shaped like Pairiquium's own ticket response — <c>{ "ticket":
    /// "...", "wsEndpoint": "..." }</c> — since that's the natural shape for a backend that's just
    /// passing the ticket through. When a preferredRegion is supplied, it's sent the same way:
    /// <c>{ "preferredRegion": "..." }</c>, assuming your backend forwards it to Pairiquium's own
    /// POST /v1/tickets untouched. If your endpoint differs, implement <see cref="ITicketProvider"/>
    /// directly instead.
    /// </summary>
    public sealed class HttpTicketProvider : ITicketProvider
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _endpoint;
        private readonly Func<HttpRequestMessage, CancellationToken, Task> _configureRequest;

        /// <param name="httpClient">Caller-owned; this provider does not dispose it.</param>
        /// <param name="endpoint">Your backend's ticket-issuing endpoint.</param>
        /// <param name="configureRequest">
        /// Optional hook to attach auth (e.g. the player's session token) or a request body before
        /// each POST.
        /// </param>
        public HttpTicketProvider(
            HttpClient httpClient,
            Uri endpoint,
            Func<HttpRequestMessage, CancellationToken, Task> configureRequest = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _configureRequest = configureRequest;
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

                if (_configureRequest != null)
                {
                    await _configureRequest(request, cancellationToken).ConfigureAwait(false);
                }

                using (HttpResponseMessage response = await _httpClient
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    // netstandard2.1's HttpContent.ReadAsStringAsync has no CancellationToken overload.
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var parsed = JsonConvert.DeserializeObject<TicketResponseModel>(body);

                    if (string.IsNullOrEmpty(parsed?.Ticket) || string.IsNullOrEmpty(parsed?.WsEndpoint))
                    {
                        throw new InvalidOperationException(
                            "Ticket endpoint response did not contain both 'ticket' and 'wsEndpoint'.");
                    }

                    return new TicketInfo(parsed.Ticket, parsed.WsEndpoint);
                }
            }
        }

        private sealed class TicketResponseModel
        {
            [JsonProperty("ticket")]
            public string Ticket { get; set; }

            [JsonProperty("wsEndpoint")]
            public string WsEndpoint { get; set; }
        }
    }
}
