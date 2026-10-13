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

    // Party actions (asyncapi create_party ... decline_invite).

    public sealed class CreatePartyMessage
    {
        [JsonProperty("action")]
        public string Action => "create_party";
    }

    public sealed class LeavePartyMessage
    {
        [JsonProperty("action")]
        public string Action => "leave_party";
    }

    public sealed class JoinPartyMessage
    {
        [JsonProperty("action")]
        public string Action => "join_party";

        [JsonProperty("payload")]
        public JoinPartyPayload Payload { get; }

        public JoinPartyMessage(string code)
        {
            Payload = new JoinPartyPayload(code);
        }
    }

    public sealed class JoinPartyPayload
    {
        [JsonProperty("code")]
        public string Code { get; }

        public JoinPartyPayload(string code)
        {
            Code = code;
        }
    }

    /// <summary>A party action whose payload is just a <c>userId</c>: kick_member, promote_leader, invite_to_party.</summary>
    public sealed class PartyUserMessage
    {
        [JsonProperty("action")]
        public string Action { get; }

        [JsonProperty("payload")]
        public PartyUserPayload Payload { get; }

        public PartyUserMessage(string action, string userId)
        {
            Action = action;
            Payload = new PartyUserPayload(userId);
        }
    }

    public sealed class PartyUserPayload
    {
        [JsonProperty("userId")]
        public string UserId { get; }

        public PartyUserPayload(string userId)
        {
            UserId = userId;
        }
    }

    /// <summary>accept_invite and decline_invite: both just name the party from a PARTY_INVITE.</summary>
    public sealed class PartyInviteReplyMessage
    {
        [JsonProperty("action")]
        public string Action { get; }

        [JsonProperty("payload")]
        public PartyInviteReplyPayload Payload { get; }

        public PartyInviteReplyMessage(string action, string partyId)
        {
            Action = action;
            Payload = new PartyInviteReplyPayload(partyId);
        }
    }

    public sealed class PartyInviteReplyPayload
    {
        [JsonProperty("partyId")]
        public string PartyId { get; }

        public PartyInviteReplyPayload(string partyId)
        {
            PartyId = partyId;
        }
    }
}
