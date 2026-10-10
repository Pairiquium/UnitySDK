using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// PROTOCOL.md section 4: send heartbeat periodically while searching, and stop once the
    /// search ends (match found, cancelled, or faulted).
    /// </summary>
    [TestFixture]
    public class HeartbeatTests
    {
        [Test]
        public async Task WhileSearching_SendsHeartbeatOnEachTick()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.SentMessages.Clear();

            // The fake Delay resolves instantly, so each loop iteration is one "tick".
            // Give the background heartbeat loop a few ticks to run.
            await Task.Delay(50);
            await Task.Delay(50);

            int heartbeats = harness.CurrentTransport.SentMessages.Count(m => m.Contains("heartbeat"));
            Assert.That(heartbeats, Is.GreaterThan(0));
        }

        [Test]
        public async Task AfterMatchFound_StopsSendingHeartbeats()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage(
                "{\"action\":\"MATCH_FOUND\",\"data\":{\"metadata\":{\"matchId\":\"m-1\",\"team\":0}}}");
            await TestHarness.Pump();

            harness.CurrentTransport.SentMessages.Clear();
            await Task.Delay(50);

            Assert.That(harness.CurrentTransport.SentMessages, Is.Empty, "no more heartbeats after a match is found");
        }
    }
}
