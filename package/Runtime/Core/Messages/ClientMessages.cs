using Newtonsoft.Json;

namespace Pairiquium.Messages
{
    // Client-to-server messages (PROTOCOL.md section 3). These are only ever serialized, never
    // deserialized, so get-only properties are enough.

    public sealed class BeginSearchMessage
    {
        [JsonProperty("action")]
        public string Action => "begin_search";

        [JsonProperty("payload")]
        public BeginSearchPayload Payload { get; }

        public BeginSearchMessage(string gameMode)
        {
            Payload = new BeginSearchPayload(gameMode);
        }
    }

    public sealed class BeginSearchPayload
    {
        [JsonProperty("gameMode")]
        public string GameMode { get; }

        public BeginSearchPayload(string gameMode)
        {
            GameMode = gameMode;
        }
    }

    public sealed class CancelSearchMessage
    {
        [JsonProperty("action")]
        public string Action => "cancel_search";
    }

    public sealed class HeartbeatMessage
    {
        [JsonProperty("action")]
        public string Action => "heartbeat";
    }

    public sealed class PongMessage
    {
        [JsonProperty("action")]
        public string Action => "pong";

        [JsonProperty("payload")]
        public PongPayload Payload { get; }

        public PongMessage(string correlationId, string gameMode)
        {
            Payload = new PongPayload(correlationId, gameMode);
        }
    }

    public sealed class PongPayload
    {
        [JsonProperty("correlationId")]
        public string CorrelationId { get; }

        [JsonProperty("gameMode")]
        public string GameMode { get; }

        public PongPayload(string correlationId, string gameMode)
        {
            CorrelationId = correlationId;
            GameMode = gameMode;
        }
    }
}
