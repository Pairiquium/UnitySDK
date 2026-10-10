using System.Threading;
using System.Threading.Tasks;

namespace Pairiquium.Tickets
{
    /// <summary>
    /// Supplies tickets to <see cref="MatchmakingClient"/>. The SDK never holds the Pairiquium
    /// API key, so this always goes through something the game developer controls — typically
    /// their own backend, via <see cref="HttpTicketProvider"/>. Called for every connection
    /// attempt, including reconnects: tickets are single-use.
    /// </summary>
    public interface ITicketProvider
    {
        /// <param name="preferredRegion">
        /// Optional region to lock the ticket to (PROTOCOL.md section 1 / openapi.json's
        /// GenerateTicketRequest). Null/empty means no preference - the ticket endpoint picks a
        /// region via latency routing.
        /// </param>
        Task<TicketInfo> GetTicketAsync(string preferredRegion, CancellationToken cancellationToken);
    }
}
