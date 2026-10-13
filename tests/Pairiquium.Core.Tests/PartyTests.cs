using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Pairiquium.Messages;

namespace Pairiquium.Core.Tests
{
    /// <summary>
    /// Parties: connect first (ConnectAsync), form a party, then the leader's begin_search queues
    /// everyone. See asyncapi create_party ... PARTY_INVITE.
    /// </summary>
    [TestFixture]
    public class PartyTests
    {
        private static string PartyUpdate(string status, string gameMode = null) =>
            "{\"action\":\"PARTY_UPDATE\",\"data\":{\"party\":{\"partyId\":\"p-1\",\"joinCode\":\"K7M2QX\"," +
            "\"leaderUserId\":\"alice\",\"members\":[{\"userId\":\"alice\"},{\"userId\":\"bob\"}]," +
            $"\"status\":\"{status}\"" + (gameMode == null ? "" : $",\"gameMode\":\"{gameMode}\"") + "}}}";

        private const string SearchStarted =
            "{\"action\":\"SEARCH_STARTED\",\"data\":{\"gameMode\":\"3v3\",\"startedAt\":1700000000,\"partyId\":\"p-1\"}}";

        private const string MatchFound =
            "{\"action\":\"MATCH_FOUND\",\"data\":{\"metadata\":{\"matchId\":\"m-1\",\"team\":0}}}";

        private static async Task<TestHarness> ConnectedAsync()
        {
            var harness = new TestHarness();
            await harness.Client.ConnectAsync();
            return harness;
        }

        [Test]
        public async Task ConnectAsync_ConnectsWithoutSearching()
        {
            using var harness = await ConnectedAsync();

            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected));
            Assert.That(harness.CurrentTransport.SentMessages, Is.Empty);
        }

        [Test]
        public async Task ConnectAsync_WhenTicketFails_ThrowsAndClosesInsteadOfRetrying()
        {
            var tickets = new Fakes.FakeTicketProvider();
            tickets.EnqueueFailure(new System.InvalidOperationException("backend down"));
            using var harness = new TestHarness(tickets);

            Assert.ThrowsAsync<System.InvalidOperationException>(() => harness.Client.ConnectAsync());
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
        }

        [Test]
        public async Task PartyActions_SendTheDocumentedFrames()
        {
            using var harness = await ConnectedAsync();
            var c = harness.Client;

            await c.CreatePartyAsync();
            await c.JoinPartyAsync("K7M2QX");
            await c.InviteToPartyAsync("bob");
            await c.KickMemberAsync("carol");
            await c.PromoteLeaderAsync("bob");
            await c.AcceptInviteAsync("p-1");
            await c.DeclineInviteAsync("p-2");
            await c.LeavePartyAsync();

            Assert.That(harness.CurrentTransport.SentMessages, Is.EqualTo(new[]
            {
                "{\"action\":\"create_party\"}",
                "{\"action\":\"join_party\",\"payload\":{\"code\":\"K7M2QX\"}}",
                "{\"action\":\"invite_to_party\",\"payload\":{\"userId\":\"bob\"}}",
                "{\"action\":\"kick_member\",\"payload\":{\"userId\":\"carol\"}}",
                "{\"action\":\"promote_leader\",\"payload\":{\"userId\":\"bob\"}}",
                "{\"action\":\"accept_invite\",\"payload\":{\"partyId\":\"p-1\"}}",
                "{\"action\":\"decline_invite\",\"payload\":{\"partyId\":\"p-2\"}}",
                "{\"action\":\"leave_party\"}"
            }));
        }

        [Test]
        public async Task PartyUpdate_IsExposedAsPartyAndRaisesEvent()
        {
            using var harness = await ConnectedAsync();
            PartyState seen = null;
            harness.Client.PartyUpdated += p => seen = p;

            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            await TestHarness.Pump();

            Assert.That(harness.Client.Party, Is.SameAs(seen));
            Assert.That(seen.JoinCode, Is.EqualTo("K7M2QX"));
            Assert.That(seen.LeaderUserId, Is.EqualTo("alice"));
            Assert.That(seen.Members.Select(m => m.UserId), Is.EqualTo(new[] { "alice", "bob" }));
            Assert.That(seen.PartyStatus, Is.EqualTo(PartyStatus.Lobby));
        }

        [Test]
        public async Task LeaderBeginSearch_ReusesTheConnectionAndKeepsTheParty()
        {
            using var harness = await ConnectedAsync();
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            await TestHarness.Pump();

            await harness.Client.BeginSearchAsync("3v3");

            Assert.That(harness.Transports.Count, Is.EqualTo(1), "must not replace the connection");
            Assert.That(harness.TicketProvider.CallCount, Is.EqualTo(1));
            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("begin_search"));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
            Assert.That(harness.Client.Party, Is.Not.Null);
        }

        [Test]
        public async Task Member_StartsSearchingAndHeartbeatsOnSearchStarted()
        {
            using var harness = await ConnectedAsync();
            SearchStartedData started = null;
            harness.Client.SearchStarted += d => started = d;

            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            harness.CurrentTransport.EnqueueMessage(SearchStarted);
            await TestHarness.Pump();

            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
            Assert.That(started.PartyId, Is.EqualTo("p-1"));
            Assert.That(started.GameMode, Is.EqualTo("3v3"));
            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("heartbeat"));
        }

        [Test]
        public async Task Ping_FromAMemberIsAnsweredWithThePartysGameMode()
        {
            using var harness = await ConnectedAsync();

            harness.CurrentTransport.EnqueueMessage(PartyUpdate("STARTING", "ranked-ping"));
            harness.CurrentTransport.EnqueueMessage("{\"action\":\"PING\",\"data\":{\"correlationId\":\"c-9\"}}");
            await TestHarness.Pump();

            string pong = harness.CurrentTransport.SentMessages.Single(m => m.Contains("\"pong\""));
            Assert.That(pong, Does.Contain("\"correlationId\":\"c-9\""));
            Assert.That(pong, Does.Contain("\"gameMode\":\"ranked-ping\""));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected), "not searching until SEARCH_STARTED");
        }

        [Test]
        public async Task MatchFound_ReturnsToConnectedAndKeepsTheConnectionForTheNextSearch()
        {
            using var harness = await ConnectedAsync();
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            await harness.Client.BeginSearchAsync("3v3");

            MatchFoundMetadata match = null;
            harness.Client.MatchFound += m => match = m;
            harness.CurrentTransport.EnqueueMessage(MatchFound);
            await TestHarness.Pump();

            Assert.That(match.MatchId, Is.EqualTo("m-1"));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected));

            await harness.Client.BeginSearchAsync("3v3");

            Assert.That(harness.Transports.Count, Is.EqualTo(1));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
        }

        [TestCase("MEMBER_LEFT")]
        [TestCase("MEMBER_DISCONNECTED")]
        [TestCase("START_FAILED")]
        public async Task PartySearchCancelled_StopsSearching(string reason)
        {
            using var harness = await ConnectedAsync();
            PartySearchCancelledData cancelled = null;
            harness.Client.PartySearchCancelled += d => cancelled = d;

            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            harness.CurrentTransport.EnqueueMessage(SearchStarted);
            await TestHarness.Pump();
            harness.CurrentTransport.EnqueueMessage(
                $"{{\"action\":\"PARTY_SEARCH_CANCELLED\",\"data\":{{\"reason\":\"{reason}\",\"userId\":\"bob\"}}}}");
            await TestHarness.Pump();

            Assert.That(cancelled.Reason, Is.EqualTo(reason));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected));
        }

        [Test]
        public async Task PartyReturningToLobbyWithoutAMatch_StopsSearching()
        {
            using var harness = await ConnectedAsync();
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            await TestHarness.Pump();
            await harness.Client.BeginSearchAsync("ranked-ping");

            // A stale lobby update must not undo a search that has only just begun...
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            await TestHarness.Pump();
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));

            // ...but the ping check expiring does.
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("STARTING", "ranked-ping"));
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            await TestHarness.Pump();
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected));
        }

        [Test]
        public async Task PartyLeft_ClearsThePartyAndStopsSearching()
        {
            using var harness = await ConnectedAsync();
            PartyLeftData left = null;
            harness.Client.PartyLeft += d => left = d;

            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            harness.CurrentTransport.EnqueueMessage(SearchStarted);
            harness.CurrentTransport.EnqueueMessage("{\"action\":\"PARTY_LEFT\",\"data\":{\"partyId\":\"p-1\",\"reason\":\"KICKED\"}}");
            await TestHarness.Pump();

            Assert.That(left.Reason, Is.EqualTo("KICKED"));
            Assert.That(harness.Client.Party, Is.Null);
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected));
        }

        [Test]
        public async Task PartyInvite_RaisesEvent()
        {
            using var harness = await ConnectedAsync();
            PartyInviteData invite = null;
            harness.Client.PartyInviteReceived += d => invite = d;

            harness.CurrentTransport.EnqueueMessage("{\"action\":\"PARTY_INVITE\",\"data\":{\"partyId\":\"p-7\",\"fromUserId\":\"alice\"}}");
            await TestHarness.Pump();

            Assert.That(invite.PartyId, Is.EqualTo("p-7"));
            Assert.That(invite.FromUserId, Is.EqualTo("alice"));
        }

        [TestCase("NOT_PARTY_LEADER")]
        [TestCase("PARTY_TOO_LARGE")]
        [TestCase("SEARCH_START_FAILED")]
        public async Task ErrorRejectingTheSearch_LeavesSearching(string code)
        {
            using var harness = await ConnectedAsync();
            ErrorData error = null;
            harness.Client.ErrorReceived += (e, _) => error = e;
            await harness.Client.BeginSearchAsync("3v3");

            harness.CurrentTransport.EnqueueMessage($"{{\"action\":\"ERROR\",\"data\":{{\"code\":\"{code}\",\"message\":\"no\"}}}}");
            await TestHarness.Pump();

            Assert.That(error.Code, Is.EqualTo(code));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Connected));
        }

        [Test]
        public async Task PartyError_WhileSearchingFromAnotherAction_DoesNotStopTheSearch()
        {
            using var harness = await ConnectedAsync();
            await harness.Client.BeginSearchAsync("3v3");
            harness.CurrentTransport.EnqueueMessage(SearchStarted);
            await TestHarness.Pump();

            harness.CurrentTransport.EnqueueMessage("{\"action\":\"ERROR\",\"data\":{\"code\":\"NOT_PARTY_LEADER\",\"message\":\"kick\"}}");
            await TestHarness.Pump();

            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Searching));
        }

        [Test]
        public async Task DroppedConnection_WithAParty_DoesNotReconnectAndClearsTheParty()
        {
            using var harness = await ConnectedAsync();
            harness.CurrentTransport.EnqueueMessage(PartyUpdate("LOBBY"));
            harness.CurrentTransport.EnqueueMessage(SearchStarted);
            await TestHarness.Pump();

            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.Transports.Count, Is.EqualTo(1), "must not reconnect and search alone");
            Assert.That(harness.Client.Party, Is.Null);
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
        }

        [Test]
        public async Task DroppedConnection_WhileOnlyConnected_DoesNotReconnect()
        {
            using var harness = await ConnectedAsync();

            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.Transports.Count, Is.EqualTo(1));
            Assert.That(harness.Client.State, Is.EqualTo(MatchmakingConnectionState.Closed));
        }

        [Test]
        public async Task SoloSearchOnALobbyConnection_StillReconnectsWhenItDrops()
        {
            var tickets = new Fakes.FakeTicketProvider(
                Fakes.FakeTicketProvider.MakeTicket("1"),
                Fakes.FakeTicketProvider.MakeTicket("2"));
            using var harness = new TestHarness(tickets);
            await harness.Client.ConnectAsync();
            await harness.Client.BeginSearchAsync("1v1");

            harness.CurrentTransport.EnqueueClose();
            await TestHarness.Pump();
            await TestHarness.Pump();

            Assert.That(harness.Transports.Count, Is.EqualTo(2));
            Assert.That(harness.CurrentTransport.SentMessages, Has.Some.Contains("begin_search"));
        }

        [Test]
        public async Task PartyActions_WithoutAConnection_Throw()
        {
            using var harness = new TestHarness();

            Assert.ThrowsAsync<System.InvalidOperationException>(() => harness.Client.CreatePartyAsync());
        }
    }
}
