using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Pairiquium.Core.Tests.Fakes;
using Pairiquium.Tickets;

namespace Pairiquium.Core.Tests
{
    [TestFixture]
    public class HttpTicketProviderTests
    {
        [Test]
        public async Task GetTicketAsync_WithPreferredRegion_SendsItInRequestBody()
        {
            var handler = new FakeHttpMessageHandler();
            using var httpClient = new HttpClient(handler);
            var provider = new HttpTicketProvider(httpClient, new Uri("http://localhost/matchmaking/ticket"));

            TicketInfo ticket = await provider.GetTicketAsync("us-east-1", CancellationToken.None);

            Assert.That(handler.LastRequestBody, Does.Contain("\"preferredRegion\":\"us-east-1\""));
            Assert.That(ticket.Ticket, Is.EqualTo("t-1"));
        }

        [Test]
        public async Task GetTicketAsync_WithoutPreferredRegion_SendsNoBody()
        {
            var handler = new FakeHttpMessageHandler();
            using var httpClient = new HttpClient(handler);
            var provider = new HttpTicketProvider(httpClient, new Uri("http://localhost/matchmaking/ticket"));

            await provider.GetTicketAsync(null, CancellationToken.None);

            Assert.That(handler.LastRequest.Content, Is.Null);
        }

        [Test]
        public void GetTicketAsync_MissingWsEndpointInResponse_Throws()
        {
            var handler = new FakeHttpMessageHandler { ResponseBody = "{\"ticket\":\"t-1\"}" };
            using var httpClient = new HttpClient(handler);
            var provider = new HttpTicketProvider(httpClient, new Uri("http://localhost/matchmaking/ticket"));

            Assert.That(
                async () => await provider.GetTicketAsync(null, CancellationToken.None),
                Throws.InvalidOperationException);
        }
    }
}
