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

    /// <summary>Acknowledges that a search is registered and queued (asyncapi SEARCH_STARTED).</summary>
    public sealed class SearchStartedMessage : ServerMessage
    {
        [JsonProperty("data")]
        public SearchStartedData Data { get; private set; }
    }

    public sealed class SearchStartedData
    {
        [JsonProperty("gameMode")]
        public string GameMode { get; private set; }

        /// <summary>Epoch seconds: the time the wait is measured from.</summary>
        [JsonProperty("startedAt")]
        public double StartedAt { get; private set; }

        /// <summary>Set when a party search started; every member receives it.</summary>
        [JsonProperty("partyId")]
        public string PartyId { get; private set; }
    }

    public sealed class PartyUpdateMessage : ServerMessage
    {
        [JsonProperty("data")]
        public PartyUpdateData Data { get; private set; }
    }

    public sealed class PartyUpdateData
    {
        [JsonProperty("party")]
        public PartyState Party { get; private set; }
    }

    /// <summary>
    /// A party's full state, as sent in every PARTY_UPDATE. Render from it directly rather than
    /// patching a previous copy.
    /// </summary>
    public sealed class PartyState
    {
        [JsonProperty("partyId")]
        public string PartyId { get; private set; }

        /// <summary>Share with friends so they can <c>join_party</c>.</summary>
        [JsonProperty("joinCode")]
        public string JoinCode { get; private set; }

        [JsonProperty("leaderUserId")]
        public string LeaderUserId { get; private set; }

        [JsonProperty("members")]
        public IList<PartyMember> Members { get; private set; } = new List<PartyMember>();

        [JsonProperty("status")]
        public string Status { get; private set; }

        /// <summary>Typed form of <see cref="Status"/>; unknown values map to <see cref="PartyStatus.Unknown"/>.</summary>
        [JsonIgnore]
        public PartyStatus PartyStatus
        {
            get
            {
                switch (Status)
                {
                    case "LOBBY": return PartyStatus.Lobby;
                    case "STARTING": return PartyStatus.Starting;
                    case "SEARCHING": return PartyStatus.Searching;
                    default: return PartyStatus.Unknown;
                }
            }
        }

        /// <summary>Present while <see cref="PartyStatus.Starting"/> (a ping-based party measuring latency).</summary>
        [JsonProperty("gameMode")]
        public string GameMode { get; private set; }
    }

    public sealed class PartyMember
    {
        [JsonProperty("userId")]
        public string UserId { get; private set; }
    }

    public enum PartyStatus
    {
        Unknown = 0,
        /// <summary>Forming up; membership can change.</summary>
        Lobby,
        /// <summary>A ping-based party is measuring each member's latency; membership is locked.</summary>
        Starting,
        /// <summary>Queued; membership is locked.</summary>
        Searching
    }

    /// <summary>You are no longer in a party.</summary>
    public sealed class PartyLeftMessage : ServerMessage
    {
        [JsonProperty("data")]
        public PartyLeftData Data { get; private set; }
    }

    public sealed class PartyLeftData
    {
        [JsonProperty("partyId")]
        public string PartyId { get; private set; }

        /// <summary><c>LEFT</c> or <c>KICKED</c>.</summary>
        [JsonProperty("reason")]
        public string Reason { get; private set; }
    }

    /// <summary>The party's search stopped before a match was made.</summary>
    public sealed class PartySearchCancelledMessage : ServerMessage
    {
        [JsonProperty("data")]
        public PartySearchCancelledData Data { get; private set; }
    }

    public sealed class PartySearchCancelledData
    {
        /// <summary>
        /// <c>LEADER_CANCELLED</c>, <c>MEMBER_LEFT</c>, <c>MEMBER_KICKED</c>,
        /// <c>MEMBER_DISCONNECTED</c> or <c>START_FAILED</c>.
        /// </summary>
        [JsonProperty("reason")]
        public string Reason { get; private set; }

        /// <summary>The member the reason is about, when there is one.</summary>
        [JsonProperty("userId")]
        public string UserId { get; private set; }
    }

    public sealed class PartyInviteMessage : ServerMessage
    {
        [JsonProperty("data")]
        public PartyInviteData Data { get; private set; }
    }

    /// <summary>An invite to join a party; valid for two minutes. Pass <see cref="PartyId"/> to accept or decline.</summary>
    public sealed class PartyInviteData
    {
        [JsonProperty("partyId")]
        public string PartyId { get; private set; }

        [JsonProperty("fromUserId")]
        public string FromUserId { get; private set; }
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

        // Every property here has a Newtonsoft-only private setter (so a caller can't fabricate
        // a fake server error), which also rules out a plain object initializer. This is the one
        // sanctioned way to build an ErrorData that didn't come from the wire - used for
        // SDK-local synthetic errors such as ErrorCodes.ConnectionFailed.
        internal static ErrorData Create(string code, string message) =>
            new ErrorData { Code = code, Message = message };
    }
}
