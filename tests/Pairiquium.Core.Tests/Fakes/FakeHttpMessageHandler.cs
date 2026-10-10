using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pairiquium.Core.Tests.Fakes
{
    /// <summary>
    /// Captures the last outgoing request (and its body) for HttpTicketProvider tests, instead
    /// of hitting a real network endpoint.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastRequestBody { get; private set; }
        public string ResponseBody { get; set; } =
            "{\"ticket\":\"t-1\",\"wsEndpoint\":\"wss://ws.eu-west-2.pairiquium.com\"}";
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync().ConfigureAwait(false)
                : null;

            return new HttpResponseMessage(ResponseStatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
