using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pairiquium.Messages
{
    // Server-to-client messages (PROTOCOL.md section 3). Deserialized via ServerMessageConverter,
    // which picks the concrete type from the "action" field. Unrecognised fields are ignored by
    // Newtonsoft's default behaviour, matching the spec's forward-compatibility rule.

    public abstract class ServerMessage
    {
        [JsonProperty("action")]
        public string Action { get; protected set; }
    }

    /// <summary>
    /// An action the SDK doesn't recognise. Per PROTOCOL.md's versioning rules, new messages may
    /// be added in additive (minor/patch) spec releases and should be ignored, not treated as errors.
    /// </summary>
    public sealed class UnknownServerMessage : ServerMessage
    {
    }

    public sealed class MatchFoundMessage : ServerMessage
    {
        [JsonProperty("data")]
        public MatchFoundData Data { get; private set; }
    }

    public sealed class MatchFoundData
    {
        [JsonProperty("metadata")]
        public MatchFoundMetadata Metadata { get; private set; }
    }

    public sealed class MatchFoundMetadata
    {
        [JsonProperty("matchId")]
        public string MatchId { get; private set; }

        [JsonProperty("team")]
        public long Team { get; private set; }
    }

    public sealed class PingMessage : ServerMessage
    {
        [JsonProperty("data")]
        public PingData Data { get; private set; }
    }

    public sealed class PingData
    {
        [JsonProperty("correlationId")]
        public string CorrelationId { get; private set; }
    }

    public sealed class SearchSupersededMessage : ServerMessage
    {
    }

    public sealed class CloseConnectionMessage : ServerMessage
    {
    }

    public sealed class ErrorMessage : ServerMessage
    {
        [JsonProperty("data")]
        public ErrorData Data { get; private set; }
    }

    public sealed class ErrorData
    {
        [JsonProperty("code")]
        public string Code { get; private set; }

        /// <summary>Typed form of <see cref="Code"/> - see <see cref="PairiquiumErrorCode"/>.</summary>
        [JsonIgnore]
        public PairiquiumErrorCode ErrorCode => ErrorCodes.Parse(Code);

        [JsonProperty("message")]
        public string Message { get; private set; }

        [JsonProperty("details")]
        public IDictionary<string, object> Details { get; private set; }
    }
}
