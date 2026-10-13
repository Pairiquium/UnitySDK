using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Schema;
using Newtonsoft.Json;
using NUnit.Framework;
using Pairiquium.Messages;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// Validates the SDK's message shapes against the vendored asyncapi.json (and the ticket
    /// response shape against openapi.json), so the SDK can't silently drift from the spec it
    /// claims to implement. See spec/VERSION.md for which tag these are pinned to.
    /// </summary>
    [TestFixture]
    public class AsyncApiConformanceTests
    {
        private static JsonNode LoadSpec(string fileName)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "spec", fileName);
            return JsonNode.Parse(File.ReadAllText(path));
        }

        private static JsonSchema LoadAsyncApiMessageSchema(string messageKey)
        {
            JsonNode spec = LoadSpec("asyncapi.json");
            JsonNode schemaNode = spec["components"]["messages"][messageKey]["payload"]["schema"];
            return JsonSchema.FromText(schemaNode.ToJsonString());
        }

        private static JsonSchema LoadOpenApiSchema(string schemaKey)
        {
            JsonNode spec = LoadSpec("openapi.json");
            JsonNode schemaNode = spec["components"]["schemas"][schemaKey];
            return JsonSchema.FromText(schemaNode.ToJsonString());
        }

        private static void AssertValid(JsonSchema schema, string instanceJson)
        {
            JsonNode instance = JsonNode.Parse(instanceJson);
            EvaluationResults result = schema.Evaluate(instance);
            Assert.That(result.IsValid, Is.True, $"{instanceJson} did not conform to schema");
        }

        // --- Client-to-server messages: serialize our own types and validate the wire shape. ---

        [Test]
        public void BeginSearch_ConformsToSchema()
        {
            string json = JsonConvert.SerializeObject(new BeginSearchMessage("1v1"));
            AssertValid(LoadAsyncApiMessageSchema("beginSearch"), json);
        }

        [Test]
        public void CancelSearch_ConformsToSchema()
        {
            string json = JsonConvert.SerializeObject(new CancelSearchMessage());
            AssertValid(LoadAsyncApiMessageSchema("cancelSearch"), json);
        }

        [Test]
        public void Heartbeat_ConformsToSchema()
        {
            string json = JsonConvert.SerializeObject(new HeartbeatMessage());
            AssertValid(LoadAsyncApiMessageSchema("heartbeat"), json);
        }

        [Test]
        public void Pong_ConformsToSchema()
        {
            string json = JsonConvert.SerializeObject(new PongMessage("c-1", "1v1"));
            AssertValid(LoadAsyncApiMessageSchema("pong"), json);
        }

        // --- Server-to-client messages: a raw sample frame, validated against the schema and
        // then deserialized to confirm our types round-trip it correctly. ---

        [Test]
        public void MatchFound_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"MATCH_FOUND\",\"data\":{\"metadata\":{\"matchId\":\"m-1\",\"team\":1}}}";
            AssertValid(LoadAsyncApiMessageSchema("matchFound"), json);

            var message = JsonConvert.DeserializeObject<MatchFoundMessage>(json);
            Assert.That(message.Data.Metadata.MatchId, Is.EqualTo("m-1"));
            Assert.That(message.Data.Metadata.Team, Is.EqualTo(1));
        }

        [Test]
        public void Ping_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"PING\",\"data\":{\"correlationId\":\"c-1\"}}";
            AssertValid(LoadAsyncApiMessageSchema("ping"), json);

            var message = JsonConvert.DeserializeObject<PingMessage>(json);
            Assert.That(message.Data.CorrelationId, Is.EqualTo("c-1"));
        }

        [Test]
        public void SearchSuperseded_SampleConforms()
        {
            const string json = "{\"action\":\"SEARCH_SUPERSEDED\"}";
            AssertValid(LoadAsyncApiMessageSchema("searchSuperseded"), json);
        }

        [Test]
        public void CloseConnection_SampleConforms()
        {
            const string json = "{\"action\":\"CLOSE_CONNECTION\"}";
            AssertValid(LoadAsyncApiMessageSchema("closeConnection"), json);
        }

        [Test]
        public void Error_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"ERROR\",\"data\":{\"code\":\"GAME_MODE_NOT_FOUND\",\"message\":\"no such mode\"}}";
            AssertValid(LoadAsyncApiMessageSchema("error"), json);

            var message = JsonConvert.DeserializeObject<ErrorMessage>(json);
            Assert.That(message.Data.Code, Is.EqualTo(ErrorCodes.GameModeNotFound));
        }

        // --- Parties (spec 1.1.0). ---

        [Test]
        public void PartyClientMessages_ConformToSchema()
        {
            AssertValid(LoadAsyncApiMessageSchema("createParty"), JsonConvert.SerializeObject(new CreatePartyMessage()));
            AssertValid(LoadAsyncApiMessageSchema("leaveParty"), JsonConvert.SerializeObject(new LeavePartyMessage()));
            AssertValid(LoadAsyncApiMessageSchema("joinParty"), JsonConvert.SerializeObject(new JoinPartyMessage("K7M2QX")));
            AssertValid(LoadAsyncApiMessageSchema("kickMember"), JsonConvert.SerializeObject(new PartyUserMessage("kick_member", "bob")));
            AssertValid(LoadAsyncApiMessageSchema("promoteLeader"), JsonConvert.SerializeObject(new PartyUserMessage("promote_leader", "bob")));
            AssertValid(LoadAsyncApiMessageSchema("inviteToParty"), JsonConvert.SerializeObject(new PartyUserMessage("invite_to_party", "bob")));
            AssertValid(LoadAsyncApiMessageSchema("acceptInvite"), JsonConvert.SerializeObject(new PartyInviteReplyMessage("accept_invite", "p-1")));
            AssertValid(LoadAsyncApiMessageSchema("declineInvite"), JsonConvert.SerializeObject(new PartyInviteReplyMessage("decline_invite", "p-1")));
        }

        [Test]
        public void SearchStarted_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"SEARCH_STARTED\",\"data\":{\"gameMode\":\"3v3\",\"startedAt\":1700000000.5,\"partyId\":\"p-1\"}}";
            AssertValid(LoadAsyncApiMessageSchema("searchStarted"), json);

            var message = JsonConvert.DeserializeObject<SearchStartedMessage>(json);
            Assert.That(message.Data.GameMode, Is.EqualTo("3v3"));
            Assert.That(message.Data.StartedAt, Is.EqualTo(1700000000.5));
            Assert.That(message.Data.PartyId, Is.EqualTo("p-1"));
        }

        [Test]
        public void PartyUpdate_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"PARTY_UPDATE\",\"data\":{\"party\":{\"partyId\":\"p-1\",\"joinCode\":\"K7M2QX\"," +
                "\"leaderUserId\":\"alice\",\"members\":[{\"userId\":\"alice\"}],\"status\":\"STARTING\",\"gameMode\":\"ranked\"}}}";
            AssertValid(LoadAsyncApiMessageSchema("partyUpdate"), json);

            PartyState party = JsonConvert.DeserializeObject<PartyUpdateMessage>(json).Data.Party;
            Assert.That(party.PartyStatus, Is.EqualTo(PartyStatus.Starting));
            Assert.That(party.GameMode, Is.EqualTo("ranked"));
            Assert.That(party.Members[0].UserId, Is.EqualTo("alice"));
        }

        [Test]
        public void PartyLeft_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"PARTY_LEFT\",\"data\":{\"partyId\":\"p-1\",\"reason\":\"KICKED\"}}";
            AssertValid(LoadAsyncApiMessageSchema("partyLeft"), json);
            Assert.That(JsonConvert.DeserializeObject<PartyLeftMessage>(json).Data.Reason, Is.EqualTo("KICKED"));
        }

        [Test]
        public void PartySearchCancelled_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"PARTY_SEARCH_CANCELLED\",\"data\":{\"reason\":\"MEMBER_LEFT\",\"userId\":\"bob\"}}";
            AssertValid(LoadAsyncApiMessageSchema("partySearchCancelled"), json);

            PartySearchCancelledData data = JsonConvert.DeserializeObject<PartySearchCancelledMessage>(json).Data;
            Assert.That(data.Reason, Is.EqualTo("MEMBER_LEFT"));
            Assert.That(data.UserId, Is.EqualTo("bob"));
        }

        [Test]
        public void PartyInvite_SampleConformsAndDeserializes()
        {
            const string json = "{\"action\":\"PARTY_INVITE\",\"data\":{\"partyId\":\"p-1\",\"fromUserId\":\"alice\"}}";
            AssertValid(LoadAsyncApiMessageSchema("partyInvite"), json);

            PartyInviteData data = JsonConvert.DeserializeObject<PartyInviteMessage>(json).Data;
            Assert.That(data.PartyId, Is.EqualTo("p-1"));
            Assert.That(data.FromUserId, Is.EqualTo("alice"));
        }

        [Test]
        public void EveryErrorCodeNamedInTheSpec_IsKnownToTheSdk()
        {
            string description = (string)JsonNode.Parse(File.ReadAllText(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "spec", "asyncapi.json")))
                ["components"]["messages"]["error"]["description"];

            var codes = System.Text.RegularExpressions.Regex.Matches(description, "`([A-Z_]{4,})`")
                .Select(m => m.Groups[1].Value)
                .Distinct();

            foreach (string code in codes)
            {
                Assert.That(ErrorCodes.Parse(code), Is.Not.EqualTo(PairiquiumErrorCode.Unknown), code);
            }
        }

        // --- Ticket response (openapi.json): the shape HttpTicketProvider parses. ---

        [Test]
        public void TicketResponse_SampleConformsToOpenApiSchema()
        {
            const string json = "{\"ticket\":\"t-1\",\"wsEndpoint\":\"wss://ws.eu-west-2.pairiquium.com\"}";
            AssertValid(LoadOpenApiSchema("GenerateTicketResponse"), json);
        }

    }
}
