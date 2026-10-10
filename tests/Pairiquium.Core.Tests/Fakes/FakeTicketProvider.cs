using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pairiquium.Tickets;

namespace Pairiquium.Core.Tests.Fakes
{
    /// <summary>
    /// Hands out pre-configured tickets and counts calls, so tests can assert a fresh ticket was
    /// requested for every connection attempt (PROTOCOL.md: tickets are single-use).
    /// </summary>
    internal sealed class FakeTicketProvider : ITicketProvider
    {
        private readonly Queue<TicketInfo> _tickets;
        private readonly Queue<Exception> _failures = new Queue<Exception>();
        private TicketInfo _last;

        public int CallCount { get; private set; }

        public List<string> RequestedRegions { get; } = new List<string>();

        public FakeTicketProvider(params TicketInfo[] tickets)
        {
            _tickets = new Queue<TicketInfo>(tickets);
        }

        public static TicketInfo MakeTicket(string suffix = "1") =>
            new TicketInfo($"ticket-{suffix}", "wss://ws.eu-west-2.pairiquium.com");

        /// <summary>Makes the next <see cref="GetTicketAsync"/> call throw instead of returning a ticket.</summary>
        public void EnqueueFailure(Exception exception) => _failures.Enqueue(exception);

        public Task<TicketInfo> GetTicketAsync(string preferredRegion, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedRegions.Add(preferredRegion);

            if (_failures.Count > 0)
            {
                throw _failures.Dequeue();
            }

            if (_tickets.Count > 0)
            {
                _last = _tickets.Dequeue();
            }
            else if (_last == null)
            {
                throw new InvalidOperationException("No ticket configured for FakeTicketProvider.");
            }

            return Task.FromResult(_last);
        }
    }
}
