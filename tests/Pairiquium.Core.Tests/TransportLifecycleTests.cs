using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Pairiquium.Core.Tests.Fakes;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// Every connection attempt replaces the previous transport, so the old one must be disposed
    /// (a real socket would otherwise stay open until the server closed it) and must not be able
    /// to disturb the connection that replaced it.
    /// </summary>
    [TestFixture]
    public class TransportLifecycleTests
    {
        [Test]
        public async Task BeginSearchAgain_DisposesThePreviousTransport()
        {
            var tickets = new FakeTicketProvider(FakeTicketProvider.MakeTicket("1"), FakeTicketProvider.MakeTicket("2"));
            using var harness = new TestHarness(tickets);

            await harness.Client.BeginSearchAsync("1v1");
            FakeWebSocketTransport first = harness.CurrentTransport;

            await harness.Client.BeginSearchAsync("1v1");

            Assert.That(first.IsDisposed, Is.True, "the first search's socket must not be left open");
            Assert.That(harness.CurrentTransport.IsDisposed, Is.False);
        }

        [Test]
        public async Task Reconnect_DisposesTheDroppedTransport()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            FakeWebSocketTransport dropped = harness.CurrentTransport;

            dropped.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.Transports.Count, Is.EqualTo(2));
            Assert.That(dropped.IsDisposed, Is.True);
            Assert.That(harness.CurrentTransport.IsDisposed, Is.False);
        }

        [Test]
        public async Task LateCloseFromAReplacedTransport_DoesNotDisturbTheNewConnection()
        {
            var tickets = new FakeTicketProvider(FakeTicketProvider.MakeTicket("1"), FakeTicketProvider.MakeTicket("2"));
            using var harness = new TestHarness(tickets);

            await harness.Client.BeginSearchAsync("1v1");
            FakeWebSocketTransport first = harness.CurrentTransport;
            await harness.Client.BeginSearchAsync("1v1");

            // The superseded connection finally reports it closed (the server does close it).
            // Note: with the fake, that connection's receive loop has already been cancelled, so
            // this pins the behaviour rather than proving the IsCurrent guard in MatchmakingClient
            // is what holds it; a real socket can fault under the loop in ways the fake can't.
            first.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching),
                "a stale transport closing must not flip the live search to Closed");
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(2), "and must not trigger another reconnect");
            Assert.That(harness.Transports.Count, Is.EqualTo(2));
        }
    }
}
