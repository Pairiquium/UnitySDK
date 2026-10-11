using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pairiquium
{
    public sealed class MatchmakingClientOptions
    {
        /// <summary>
        /// PROTOCOL.md: searches expire after 3 minutes without a heartbeat; 30-60s is suggested.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Backoff for reconnect attempts after an unexpected drop. PROTOCOL.md only says "don't
        /// retry in a tight loop, back off between attempts" without specifying a policy; this is
        /// this SDK's choice (exponential, capped), not a documented protocol requirement.
        /// Backoff restarts from <see cref="InitialReconnectDelay"/> once a reconnected connection
        /// has proved stable (see <see cref="MaxReconnectAttempts"/>).
        /// </summary>
        public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(16);

        /// <summary>
        /// Caps consecutive reconnect attempts after an unexpected drop. The count restarts once a
        /// connection proves stable, meaning it stays up for a full <see cref="HeartbeatInterval"/>
        /// and accepts a heartbeat, so a long search that reconnects successfully many times is
        /// not cut off. Null (default) means retry indefinitely until
        /// <see cref="MatchmakingClient.CancelSearchAsync"/> or <see cref="MatchmakingClient.CloseAsync"/>
        /// is called.
        /// </summary>
        public int? MaxReconnectAttempts { get; set; }

        /// <summary>
        /// Injectable delay seam so heartbeat/backoff tests don't sleep in real time. Defaults to
        /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
        /// </summary>
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } =
            (delay, ct) => Task.Delay(delay, ct);
    }
}
