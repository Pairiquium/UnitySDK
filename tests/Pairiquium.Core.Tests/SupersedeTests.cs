using System.Threading.Tasks;
using NUnit.Framework;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// PROTOCOL.md section 4: SEARCH_SUPERSEDED means a newer connection took over; this
    /// connection is closed by the server and must not auto-reconnect.
    /// </summary>
    [TestFixture]
    public class SupersedeTests
    {
        [Test]
        public async Task SearchSuperseded_ClosesAndDoesNotReconnect()
        {
            using var harness = new TestHarness();
            bool raised = false;
            harness.Client.SearchSuperseded += () => raised = true;

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage("{\"action\":\"SEARCH_SUPERSEDED\"}");
            await TestHarness.Pump();

            Assert.That(raised, Is.True);
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1));
            Assert.That(harness.Transports.Count, Is.EqualTo(1));
        }
    }
}
