using System;
using System.Text;
using System.Threading.Tasks;
using Pairiquium.Messages;
using Pairiquium.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Pairiquium.Samples
{
    /// <summary>
    /// The party half of the matchmaking leg: create/join (by code or by userId invite)/leave a
    /// party and see its members. It builds its own uGUI at runtime instead of living in
    /// ArenaDuel.unity, so the sample scene needs no edits to gain parties (and a hand-edited scene
    /// is exactly the kind of change that fails silently - see AGENTS.md). In your own game this is
    /// the part you'd replace with your friends list or lobby UI; the calls on
    /// <see cref="MatchmakingBehaviour"/> are the whole integration.
    ///
    /// What it demonstrates about the SDK:
    /// <list type="bullet">
    /// <item>Parties live on a connection opened ahead of time with <c>ConnectAsync</c>; the first
    /// party action here opens it. A later <c>BeginSearchAsync</c> reuses that same connection.</item>
    /// <item>Everything arrives as events carrying the full party state, so the UI just re-renders
    /// <c>Party</c> each time and never patches.</item>
    /// <item>Only the leader searches. <see cref="IsLeader"/> compares <c>LeaderUserId</c> with this
    /// client's own userId (see <see cref="ArenaTicketProvider"/>).</item>
    /// </list>
    /// </summary>
    internal sealed class ArenaPartyPanel : MonoBehaviour
    {
        private MatchmakingBehaviour _matchmaking;
        private ArenaTicketProvider _tickets;
        private Func<string> _region;

        private Text _partyText;
        private Text _noticeText;
        private InputField _codeInput;
        private InputField _inviteInput;
        private GameObject _inviteRow;
        private Text _inviteText;
        private PartyInviteData _pendingInvite;
        private Button[] _lobbyButtons;

        /// <summary>Raised whenever party membership or leadership may have changed.</summary>
        public event Action Changed;

        public bool InParty => _matchmaking != null && _matchmaking.Party != null;

        public bool IsLeader =>
            InParty && _tickets.UserId != null && _matchmaking.Party.LeaderUserId == _tickets.UserId;

        /// <param name="parent">The lobby canvas: the panel hides with it when the arena opens.</param>
        /// <param name="region">The region picked in the lobby; null means automatic.</param>
        public static ArenaPartyPanel Create(
            Transform parent, MatchmakingBehaviour matchmaking, ArenaTicketProvider tickets, Func<string> region)
        {
            var go = new GameObject("PartyPanel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<ArenaPartyPanel>();
            panel._matchmaking = matchmaking;
            panel._tickets = tickets;
            panel._region = region;
            panel.Build();

            matchmaking.OnPartyUpdated.AddListener(_ => panel.Refresh());
            matchmaking.OnPartyLeft.AddListener(data =>
            {
                panel._noticeText.text = data.Reason == "KICKED" ? "You were removed from the party." : string.Empty;
                panel.Refresh();
            });
            matchmaking.OnPartySearchCancelled.AddListener(data =>
            {
                panel._noticeText.text = $"Party search cancelled ({data.Reason}).";
            });
            matchmaking.OnPartyInvite.AddListener(invite =>
            {
                panel._pendingInvite = invite;
                panel.Refresh();
            });
            matchmaking.OnStateChanged.AddListener(_ => panel.Refresh());
            panel.Refresh();
            return panel;
        }

        private void Build()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-12f, -12f);

            var background = gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rect.sizeDelta = new Vector2(280f, 0f);

            _partyText = MakeText("Party", 15, Color.white);
            _noticeText = MakeText(string.Empty, 13, new Color(1f, 0.8f, 0.4f));

            Button create = MakeButton("Create party", OnCreate);
            _codeInput = MakeInput("Join code");
            Button join = MakeButton("Join with code", OnJoin);
            _inviteInput = MakeInput("Invite a userId (leader)");
            Button invite = MakeButton("Invite", OnInvite);
            Button leave = MakeButton("Leave party", OnLeave);
            _lobbyButtons = new[] { create, join, invite, leave };

            _inviteRow = new GameObject("InviteRow", typeof(RectTransform));
            _inviteRow.transform.SetParent(transform, false);
            var rowLayout = _inviteRow.AddComponent<VerticalLayoutGroup>();
            rowLayout.spacing = 4f;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            _inviteText = MakeText(string.Empty, 14, Color.white, _inviteRow.transform);
            MakeButton("Accept invite", OnAcceptInvite, _inviteRow.transform);
            MakeButton("Decline", OnDeclineInvite, _inviteRow.transform);
        }

        private void Refresh()
        {
            PartyState party = _matchmaking.Party;
            var sb = new StringBuilder();

            if (party == null)
            {
                sb.Append("Not in a party");
            }
            else
            {
                sb.Append($"Party code: {party.JoinCode}  ({party.PartyStatus})");
                foreach (PartyMember member in party.Members)
                {
                    bool leader = member.UserId == party.LeaderUserId;
                    bool me = member.UserId == _tickets.UserId;
                    sb.Append($"\n  {member.UserId}{(leader ? " [leader]" : string.Empty)}{(me ? " (you)" : string.Empty)}");
                }
            }

            if (_tickets.UserId != null)
            {
                sb.Append($"\nYour id: {_tickets.UserId}");
            }

            _partyText.text = sb.ToString();

            // Membership is locked while the party is queued or measuring; the server would refuse
            // these anyway, but greying them out says why.
            bool locked = _matchmaking.State == MatchmakingConnectionState.Searching
                || (party != null && party.PartyStatus != PartyStatus.Lobby);
            foreach (Button button in _lobbyButtons)
            {
                button.interactable = !locked;
            }

            _inviteRow.SetActive(_pendingInvite != null && party == null);
            if (_pendingInvite != null)
            {
                _inviteText.text = $"{_pendingInvite.FromUserId} invited you to a party";
            }

            Changed?.Invoke();
        }

        // These are UI event handlers, so async void with a try/catch is the right shape (see
        // ArenaDuelSample.BeginSearch for why).
        private async void OnCreate() => await Run(async () =>
        {
            await EnsureConnectedAsync();
            await _matchmaking.CreatePartyAsync();
        });

        private async void OnJoin() => await Run(async () =>
        {
            string code = _codeInput.text.Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                _noticeText.text = "Enter a join code first.";
                return;
            }

            await EnsureConnectedAsync();
            await _matchmaking.JoinPartyAsync(code);
        });

        private async void OnInvite() => await Run(async () =>
        {
            string userId = _inviteInput.text.Trim();
            if (userId.Length == 0)
            {
                _noticeText.text = "Enter the userId to invite.";
                return;
            }

            await _matchmaking.InviteToPartyAsync(userId);
        });

        private async void OnLeave() => await Run(() => _matchmaking.LeavePartyAsync());

        private async void OnAcceptInvite() => await Run(async () =>
        {
            PartyInviteData invite = _pendingInvite;
            _pendingInvite = null;
            if (invite != null)
            {
                await _matchmaking.AcceptInviteAsync(invite.PartyId);
            }
        });

        private async void OnDeclineInvite() => await Run(async () =>
        {
            PartyInviteData invite = _pendingInvite;
            _pendingInvite = null;
            if (invite != null)
            {
                await _matchmaking.DeclineInviteAsync(invite.PartyId);
            }

            Refresh();
        });

        private async Task Run(Func<Task> action)
        {
            _noticeText.text = string.Empty;
            try
            {
                await action();
            }
            catch (InvalidOperationException)
            {
                // Sent while not connected (e.g. leave_party after the connection dropped).
                _noticeText.text = "Not connected.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _noticeText.text = $"Couldn't connect: {ex.Message}";
            }
        }

        // Parties need a connection before there is a search. Everyone in a party must hold tickets
        // for the same region, so this uses the region picked in the lobby: pick the same explicit
        // one on every client rather than Automatic.
        private async Task EnsureConnectedAsync()
        {
            MatchmakingConnectionState state = _matchmaking.State;
            if (state == MatchmakingConnectionState.Connected
                || state == MatchmakingConnectionState.Searching
                || state == MatchmakingConnectionState.Connecting)
            {
                return;
            }

            await _matchmaking.ConnectAsync(_region());
        }

        // --- Tiny uGUI helpers. ---

        private static Font _font;

        private static Font DefaultFont()
        {
            if (_font == null)
            {
#if UNITY_2022_2_OR_NEWER
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }

            return _font;
        }

        private Text MakeText(string text, int size, Color color, Transform parent = null)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var label = go.AddComponent<Text>();
            label.font = DefaultFont();
            label.fontSize = size;
            label.color = color;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private Button MakeButton(string label, Action onClick, Transform parent = null)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.25f, 0.35f, 0.5f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            go.AddComponent<LayoutElement>().minHeight = 28f;

            Text text = MakeText(label, 14, Color.white, go.transform);
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            return button;
        }

        private InputField MakeInput(string placeholder)
        {
            var go = new GameObject(placeholder, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.9f);
            go.AddComponent<LayoutElement>().minHeight = 28f;
            var input = go.AddComponent<InputField>();
            input.targetGraphic = image;

            Text text = MakeText(string.Empty, 14, Color.black, go.transform);
            Text hint = MakeText(placeholder, 14, new Color(0.35f, 0.35f, 0.35f), go.transform);
            hint.fontStyle = FontStyle.Italic;
            foreach (Text t in new[] { text, hint })
            {
                t.alignment = TextAnchor.MiddleLeft;
                var r = (RectTransform)t.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(6f, 0f);
                r.offsetMax = new Vector2(-6f, 0f);
            }

            input.textComponent = text;
            input.placeholder = hint;
            return input;
        }
    }
}
