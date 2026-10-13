using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pairiquium.Messages
{
    /// <summary>
    /// Picks the concrete <see cref="ServerMessage"/> subtype from the "action" field. Must be
    /// supplied explicitly via <see cref="JsonSerializerSettings"/> when deserializing a top-level
    /// server frame (not via a [JsonConverter] attribute on the base class) — otherwise Newtonsoft
    /// would recurse back into this converter for the concrete subtype, since attribute-based
    /// converters are inherited.
    /// </summary>
    internal sealed class ServerMessageConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(ServerMessage);

        public override bool CanWrite => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            JObject obj = JObject.Load(reader);
            string action = (string)obj["action"];

            switch (action)
            {
                case "MATCH_FOUND":
                    return obj.ToObject<MatchFoundMessage>(serializer);
                case "PING":
                    return obj.ToObject<PingMessage>(serializer);
                case "SEARCH_STARTED":
                    return obj.ToObject<SearchStartedMessage>(serializer);
                case "PARTY_UPDATE":
                    return obj.ToObject<PartyUpdateMessage>(serializer);
                case "PARTY_LEFT":
                    return obj.ToObject<PartyLeftMessage>(serializer);
                case "PARTY_SEARCH_CANCELLED":
                    return obj.ToObject<PartySearchCancelledMessage>(serializer);
                case "PARTY_INVITE":
                    return obj.ToObject<PartyInviteMessage>(serializer);
                case "SEARCH_SUPERSEDED":
                    return obj.ToObject<SearchSupersededMessage>(serializer);
                case "CLOSE_CONNECTION":
                    return obj.ToObject<CloseConnectionMessage>(serializer);
                case "ERROR":
                    return obj.ToObject<ErrorMessage>(serializer);
                default:
                    return obj.ToObject<UnknownServerMessage>(serializer);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("Server messages are never serialized by the client.");
        }
    }
}
