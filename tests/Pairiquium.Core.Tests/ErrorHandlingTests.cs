using System.Threading.Tasks;
using NUnit.Framework;
using Pairiquium.Messages;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// PROTOCOL.md section 6: terminal codes close the connection and must not trigger
    /// auto-reconnect; non-terminal codes leave the connection open.
    /// </summary>
    [TestFixture]
    public class ErrorHandlingTests
    {
        [TestCase(ErrorCodes.BillingAccessDenied)]
        [TestCase(ErrorCodes.TokenQuotaExceeded)]
        [TestCase(ErrorCodes.WaitlistAccessDenied)]
        [TestCase(ErrorCodes.PlayerAttributeRequired)]
        public async Task TerminalError_FaultsAndDoesNotReconnect(string code)
        {
            using var harness = new TestHarness();
            ErrorData received = null;
            bool? terminalFlag = null;
            harness.Client.ErrorReceived += (data, isTerminal) => { received = data; terminalFlag = isTerminal; };

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage(
                $"{{\"action\":\"ERROR\",\"data\":{{\"code\":\"{code}\",\"message\":\"nope\"}}}}");
            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();

            Assert.That(received?.Code, Is.EqualTo(code));
            Assert.That(terminalFlag, Is.True);
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Faulted));
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1), "must not reconnect after a terminal error");
            Assert.That(harness.Transports.Count, Is.EqualTo(1));
        }

        [TestCase(ErrorCodes.GameModeNotFound)]
        [TestCase(ErrorCodes.GameModeDisabled)]
        [TestCase(ErrorCodes.InvalidMessage)]
        [TestCase(ErrorCodes.InternalError)]
        public async Task NonTerminalError_SurfacesEventWithoutReconnecting(string code)
        {
            using var harness = new TestHarness();
            bool? terminalFlag = null;
            harness.Client.ErrorReceived += (_, isTerminal) => terminalFlag = isTerminal;

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage(
                $"{{\"action\":\"ERROR\",\"data\":{{\"code\":\"{code}\",\"message\":\"nope\"}}}}");
            await TestHarness.Pump();

            Assert.That(terminalFlag, Is.False);
            // The server didn't close the connection for these codes, so the SDK shouldn't either.
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1));
            Assert.That(harness.Transports.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task UnknownErrorCode_TreatedAsNonTerminal()
        {
            using var harness = new TestHarness();
            bool? terminalFlag = null;
            harness.Client.ErrorReceived += (_, isTerminal) => terminalFlag = isTerminal;

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.EnqueueMessage(
                "{\"action\":\"ERROR\",\"data\":{\"code\":\"SOME_FUTURE_CODE\",\"message\":\"?\"}}");
            await TestHarness.Pump();

            Assert.That(terminalFlag, Is.False);
        }

        [Test]
        public async Task StalePong_RetriesBeginSearch()
        {
            using var harness = new TestHarness();

            await harness.Client.BeginSearchAsync("1v1");
            harness.CurrentTransport.SentMessages.Clear();

            harness.CurrentTransport.EnqueueMessage(
                $"{{\"action\":\"ERROR\",\"data\":{{\"code\":\"{ErrorCodes.StalePong}\",\"message\":\"stale\"}}}}");
            await TestHarness.Pump();

            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("begin_search"));
        }
    }
}
