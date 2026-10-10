using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pairiquium.Transport
{
    public enum TransportState
    {
        None,
        Connecting,
        Open,
        Closing,
        Closed,
        Aborted
    }

    public enum TransportCloseReason
    {
        Normal,
        Error,
        Aborted
    }

    public readonly struct TransportReceiveResult
    {
        public string Message { get; }
        public bool IsClose { get; }
        public TransportCloseReason CloseReason { get; }

        public static TransportReceiveResult ForMessage(string message) =>
            new TransportReceiveResult(message, false, TransportCloseReason.Normal);

        public static TransportReceiveResult ForClose(TransportCloseReason reason) =>
            new TransportReceiveResult(null, true, reason);

        private TransportReceiveResult(string message, bool isClose, TransportCloseReason closeReason)
        {
            Message = message;
            IsClose = isClose;
            CloseReason = closeReason;
        }
    }

    /// <summary>
    /// Abstracts the WebSocket transport so platforms without
    /// <see cref="System.Net.WebSockets.ClientWebSocket"/> (consoles) can plug in their own
    /// implementation. One JSON text frame in, one JSON text frame out; <see cref="MatchmakingClient"/>
    /// owns all protocol behaviour above this.
    /// </summary>
    public interface IWebSocketTransport : IDisposable
    {
        TransportState State { get; }

        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        Task SendAsync(string message, CancellationToken cancellationToken);

        /// <summary>
        /// Waits for the next text frame. Returns a close result (rather than throwing) when the
        /// connection ends, whether the server closed it or the transport detected a drop.
        /// </summary>
        Task<TransportReceiveResult> ReceiveAsync(CancellationToken cancellationToken);

        Task CloseAsync(CancellationToken cancellationToken);
    }
}
