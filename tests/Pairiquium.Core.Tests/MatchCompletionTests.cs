using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// A found match completes the search. The connection may close afterwards (an idle timeout,
    /// the game closing it late); that must never look like a drop to recover from, or a player
    /// who was just matched gets queued again.
    /// </summary>
    [TestFixture]
    public class MatchCompletionTests
    {
        private const string MatchFound =
            "{\"action\":\"MATCH_FOUND\",\"data\":{\"metadata\":{\"matchId\":\"m-1\",\"team\":0}}}";

        [Test]
        public async Task ConnectionClosingAfterMatchFound_DoesNotReconnectOrRequeue()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage(MatchFound);
            await TestHarness.Pump();

            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            int beginSearches = harness.Transports
                .SelectMany(t => t.SentMessages)
                .Count(m => m.Contains("begin_search"));

            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1), "must not mint another ticket after a match");
            Assert.That(harness.Transports.Count, Is.EqualTo(1), "must not open another connection after a match");
            Assert.That(beginSearches, Is.EqualTo(1), "must not search again after a match");
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
        }

        [Test]
        public async Task BeginSearchAfterAMatch_StartsANewSearch()
        {
            var tickets = new Fakes.FakeTicketProvider(
                Fakes.FakeTicketProvider.MakeTicket("1"),
                Fakes.FakeTicketProvider.MakeTicket("2"));
            using var harness = new TestHarness(tickets);

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage(MatchFound);
            await TestHarness.Pump();

            await harness.Client.BeginSearchAsync("1v1");

            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(2));
            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("begin_search"));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));

            // The new search is a normal one again: a drop reconnects.
            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();
            Assert.That(harness.Transports.Count, Is.EqualTo(3));
        }
    }
}
