using System.Linq;
using NUnit.Framework;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// PROTOCOL.md section 4: for ping-based game modes, reply to PING with pong immediately,
    /// echoing correlationId and gameMode. The SDK handles this automatically.
    /// </summary>
    [TestFixture]
    public class PingPongTests
    {
        [Test]
        public async System.Threading.Tasks.Task Ping_IsAnsweredWithMatchingPong()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("ranked-ping");
            harness.CurrentTransport.SentMessages.Clear();

            harness.CurrentTransport.EnqueueMessage("{\"action\":\"PING\",\"data\":{\"correlationId\":\"c-42\"}}");
            await TestHarness.Pump();

            string pong = harness.CurrentTransport.SentMessages.FirstOrDefault(m => m.Contains("\"pong\""));
            Assert.That(pong, Is.Not.Null);
            Assert.That(pong, Does.Contain("\"correlationId\":\"c-42\""));
            Assert.That(pong, Does.Contain("\"gameMode\":\"ranked-ping\""));
        }
    }
}
