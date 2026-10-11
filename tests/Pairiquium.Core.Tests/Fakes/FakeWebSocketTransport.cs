using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Pairiquium.Transport;

namespace Pairiquium.Core.Tests.Fakes
{
    /// <summary>
    /// Scriptable <see cref="IWebSocketTransport"/> for testing MatchmakingClient without a real
    /// socket. Tests push server frames with <see cref="EnqueueMessage"/> and simulate drops with
    /// <see cref="EnqueueClose"/>; sent frames are recorded in <see cref="SentMessages"/>.
    /// </summary>
    internal sealed class FakeWebSocketTransport : IWebSocketTransport
    {
        private readonly Channel<TransportReceiveResult> _incoming = Channel.CreateUnbounded<TransportReceiveResult>();

        public List<string> SentMessages { get; } = new List<string>();

        public List<Uri> ConnectedUris { get; } = new List<Uri>();

        public bool ThrowOnConnect { get; set; }

        public bool IsDisposed { get; private set; }

        public TransportState State { get; private set; } = TransportState.None;

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            ConnectedUris.Add(uri);

            if (ThrowOnConnect)
            {
                State = TransportState.Aborted;
                throw new InvalidOperationException("Simulated connect failure");
            }

            State = TransportState.Open;
            return Task.CompletedTask;
        }

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public async Task<TransportReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
        {
            return await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            State = TransportState.Closed;
            EnqueueClose();
            return Task.CompletedTask;
        }

        public void EnqueueMessage(string json)
        {
            _incoming.Writer.TryWrite(TransportReceiveResult.ForMessage(json));
        }

        public void EnqueueClose(TransportCloseReason reason = TransportCloseReason.Normal)
        {
            State = TransportState.Closed;
            _incoming.Writer.TryWrite(TransportReceiveResult.ForClose(reason));
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
