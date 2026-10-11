using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Pairiquium.Core.Tests.Fakes;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// PROTOCOL.md section 7: an unexpected drop mid-search reconnects with a fresh ticket and
    /// resends begin_search for the same game mode, backing off between attempts.
    /// </summary>
    [TestFixture]
    public class ReconnectTests
    {
        [Test]
        public async Task UnexpectedDrop_ReconnectsWithFreshTicketAndSameGameMode()
        {
            var ticketProvider = new FakeTicketProvider(
                FakeTicketProvider.MakeTicket("1"),
                FakeTicketProvider.MakeTicket("2"));
            using var harness = new TestHarness(ticketProvider);

            await harness.Client.BeginSearchAsync("1v1");
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1));

            // Drop without SEARCH_SUPERSEDED or a terminal ERROR first - an unexpected close.
            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(2), "should request a fresh ticket to reconnect");
            Assert.That(harness.Transports.Count, Is.EqualTo(2), "should open a new transport, not reuse the dropped one");
            Assert.That(harness.DelayCallCount, Is.GreaterThan(0), "should back off before reconnecting");

            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("begin_search"));
            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("\"1v1\""));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
        }

        [Test]
        public async Task PreferredRegion_IsSentOnEveryTicketRequestIncludingReconnects()
        {
            var ticketProvider = new FakeTicketProvider(
                FakeTicketProvider.MakeTicket("1"),
                FakeTicketProvider.MakeTicket("2"));
            using var harness = new TestHarness(ticketProvider);

            await harness.Client.BeginSearchAsync("1v1", preferredRegion: "us-east-1");
            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.TicketProvider.RequestedRegions, Is.EqualTo(new[] { "us-east-1", "us-east-1" }),
                "a reconnect must request the same region as the original search, not drop the preference");
        }

        [Test]
        public async Task CancelSearch_SuppressesReconnectAfterSubsequentDrop()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            await harness.Client.CancelSearchAsync();

            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();

            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1), "a cancelled search should not reconnect");
            Assert.That(harness.Transports.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task MaxReconnectAttempts_StopsRetrying()
        {
            var options = new MatchmakingClientOptions { MaxReconnectAttempts = 1 };
            using var harness = new TestHarness(options: options) { HoldHeartbeats = true };

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueClose(); // drop #1 -> reconnect (attempt 1)
            await TestHarness.Pump();
            await TestHarness.Pump();
            Assert.That(harness.Transports.Count, Is.EqualTo(2));

            harness.CurrentTransport.EnqueueClose(); // drop #2 -> attempt budget exhausted
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.Transports.Count, Is.EqualTo(2), "should not reconnect past MaxReconnectAttempts");
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
        }

        [Test]
        public async Task MaxReconnectAttempts_RestartsOnceAConnectionIsStable()
        {
            var options = new MatchmakingClientOptions { MaxReconnectAttempts = 1 };
            using var harness = new TestHarness(options: options);

            await harness.Client.BeginSearchAsync("1v1");

            // Each reconnect stays up long enough to send a heartbeat (the harness's heartbeat
            // wait resolves instantly), so the single-attempt budget is restored every time.
            for (int drop = 1; drop <= 3; drop++)
            {
                harness.CurrentTransport.EnqueueClose();
                await TestHarness.Pump();
                await TestHarness.Pump();

                Assert.That(harness.Transports.Count, Is.EqualTo(drop + 1), $"drop #{drop} should still reconnect");
                Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
            }
        }

        [Test]
        public async Task Backoff_RestartsFromInitialDelayAfterAStableConnection()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            for (int drop = 0; drop < 5; drop++)
            {
                harness.CurrentTransport.EnqueueClose();
                await TestHarness.Pump();
                await TestHarness.Pump();
            }

            Assert.That(harness.ReconnectDelays, Has.Count.EqualTo(5));
            Assert.That(harness.ReconnectDelays, Has.All.EqualTo(harness.Options.InitialReconnectDelay),
                "every drop after a healthy reconnect should wait the initial delay, not a grown one");
        }

        [Test]
        public async Task Backoff_KeepsGrowingWhileConnectionsNeverProveStable()
        {
            using var harness = new TestHarness { HoldHeartbeats = true };

            await harness.Client.BeginSearchAsync("1v1");
            for (int drop = 0; drop < 5; drop++)
            {
                harness.CurrentTransport.EnqueueClose();
                await TestHarness.Pump();
                await TestHarness.Pump();
            }

            Assert.That(
                harness.ReconnectDelays.ConvertAll(d => d.TotalSeconds),
                Is.EqualTo(new[] { 1.0, 2.0, 4.0, 8.0, 16.0 }),
                "a server that accepts and immediately drops must not get a tight reconnect loop");
        }
    }
}
