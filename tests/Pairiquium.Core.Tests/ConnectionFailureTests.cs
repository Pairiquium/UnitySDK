using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Pairiquium.Core.Tests.Fakes;
using Pairiquium.Messages;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// Covers the gap before any server ERROR message is possible: the ticket fetch (or the
    /// initial handshake) failing outright, e.g. because the ticket backend is unreachable. This
    /// is SDK-local (<see cref="ErrorCodes.ConnectionFailed"/>), not part of PROTOCOL.md section 6,
    /// but surfaced through the same ErrorReceived event and retried with the same backoff as a
    /// dropped connection.
    /// </summary>
    [TestFixture]
    public class ConnectionFailureTests
    {
        [Test]
        public async Task TicketFetchFailure_SurfacesErrorAndRetriesUntilSuccess()
        {
            var ticketProvider = new FakeTicketProvider(FakeTicketProvider.MakeTicket("1"));
            ticketProvider.EnqueueFailure(new InvalidOperationException("ticket backend unreachable"));
            using var harness = new TestHarness(ticketProvider);

            ErrorData received = null;
            bool? terminalFlag = null;
            harness.Client.ErrorReceived += (data, isTerminal) => { received = data; terminalFlag = isTerminal; };

            await harness.Client.BeginSearchAsync("1v1");

            Assert.That(received?.Code, Is.EqualTo(ErrorCodes.ConnectionFailed));
            Assert.That(received?.Message, Does.Contain("ticket backend unreachable"));
            Assert.That(terminalFlag, Is.False);
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(2), "should retry the ticket fetch after the first failure");
            Assert.That(harness.DelayCallCount, Is.GreaterThan(0), "should back off before retrying");
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
        }

        [Test]
        public async Task TicketFetchFailure_StopsRetryingPastMaxReconnectAttempts()
        {
            var options = new MatchmakingClientOptions { MaxReconnectAttempts = 1 };
            using var harness = new TestHarness(new FakeTicketProvider(), options);

            await harness.Client.BeginSearchAsync("1v1");

            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(2), "initial attempt + one retry, then give up");
        }
    }
}
