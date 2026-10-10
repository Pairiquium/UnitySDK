using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pairiquium.Transport;

namespace Pairiquium.Unity
{
    /// <summary>
    /// Default <see cref="IWebSocketTransport"/> for Windows and Linux desktop, backed by
    /// <see cref="System.Net.WebSockets.ClientWebSocket"/>. Not for WebGL (no raw socket access
    /// in the browser - excluded at the asmdef level) or consoles (separate, NDA'd transport
    /// implementations plug into the same <see cref="IWebSocketTransport"/> interface instead).
    /// </summary>
    public sealed class ClientWebSocketTransport : IWebSocketTransport
    {
        private const int ReceiveBufferSize = 8192;

        private ClientWebSocket _socket;

        public TransportState State
        {
            get
            {
                if (_socket == null)
                {
                    return TransportState.None;
                }

                switch (_socket.State)
                {
                    case WebSocketState.None:
                        return TransportState.None;
                    case WebSocketState.Connecting:
                        return TransportState.Connecting;
                    case WebSocketState.Open:
                        return TransportState.Open;
                    case WebSocketState.CloseSent:
                    case WebSocketState.CloseReceived:
                        return TransportState.Closing;
                    case WebSocketState.Closed:
                        return TransportState.Closed;
                    default:
                        return TransportState.Aborted;
                }
            }
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            return _socket.SendAsync(
                new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }

        public async Task<TransportReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[ReceiveBufferSize];

            using (var stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return TransportReceiveResult.ForClose(TransportCloseReason.Normal);
                    }

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string text = Encoding.UTF8.GetString(stream.ToArray());
                return TransportReceiveResult.ForMessage(text);
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _socket?.Dispose();
        }
    }
}
