using System;

namespace Pairiquium.Tickets
{
    /// <summary>
    /// The result of minting a ticket: mirrors GenerateTicketResponse in openapi.json.
    /// </summary>
    public sealed class TicketInfo
    {
        public string Ticket { get; }

        public string WsEndpoint { get; }

        public TicketInfo(string ticket, string wsEndpoint)
        {
            Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
            WsEndpoint = wsEndpoint ?? throw new ArgumentNullException(nameof(wsEndpoint));
        }
    }
}
