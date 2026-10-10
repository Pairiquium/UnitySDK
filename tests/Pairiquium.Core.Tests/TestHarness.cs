using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pairiquium.Core.Tests.Fakes;
using Pairiquium.Transport;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// Wires a MatchmakingClient to fakes with an instant (non-sleeping) delay function, so
    /// heartbeat/backoff tests run fast and deterministically.
    /// </summary>
    internal sealed class TestHarness : IDisposable
    {
        public FakeTicketProvider TicketProvider { get; }
        public List<FakeWebSocketTransport> Transports { get; } = new List<FakeWebSocketTransport>();
        public MatchmakingClient Client { get; }
        public MatchmakingClientOptions Options { get; }

        public int DelayCallCount { get; private set; }

        public TestHarness(FakeTicketProvider ticketProvider = null, MatchmakingClientOptions options = null)
        {
            TicketProvider = ticketProvider ?? new FakeTicketProvider(FakeTicketProvider.MakeTicket());
            Options = options ?? new MatchmakingClientOptions();
            Options.Delay = async (_, ct) =>
            {
                DelayCallCount++;
                // A real (if tiny) delay: Task.CompletedTask would let the heartbeat/reconnect
                // loop's await resume synchronously with no actual yield, spinning at 100% CPU
                // for as long as the loop runs (which, left undisposed, is forever).
                await Task.Delay(1, ct).ConfigureAwait(false);
            };

            Client = new MatchmakingClient(TicketProvider, CreateTransport, Options);
        }

        public FakeWebSocketTransport CurrentTransport => Transports[Transports.Count - 1];

        private IWebSocketTransport CreateTransport()
        {
            var transport = new FakeWebSocketTransport();
            Transports.Add(transport);
            return transport;
        }

        /// <summary>
        /// Lets queued continuations (the receive loop, heartbeat loop) run before assertions.
        /// </summary>
        public static Task Pump() => Task.Delay(20);

        /// <summary>
        /// Stops the client's background heartbeat/receive loops. Every test must dispose its
        /// harness, or those loops keep running (and keep calling Options.Delay) forever.
        /// </summary>
        public void Dispose() => Client.Dispose();
    }
}
