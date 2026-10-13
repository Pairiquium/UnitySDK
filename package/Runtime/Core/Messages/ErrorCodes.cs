using System;
using System.Collections.Generic;

namespace Pairiquium.Messages
{
    /// <summary>
    /// ERROR codes from PROTOCOL.md section 6.
    /// </summary>
    public static class ErrorCodes
    {
        public const string BillingAccessDenied = "BILLING_ACCESS_DENIED";
        public const string TokenQuotaExceeded = "TOKEN_QUOTA_EXCEEDED";
        public const string WaitlistAccessDenied = "WAITLIST_ACCESS_DENIED";
        public const string PlayerAttributeRequired = "PLAYER_ATTRIBUTE_REQUIRED";

        public const string GameModeNotFound = "GAME_MODE_NOT_FOUND";
        public const string GameModeDisabled = "GAME_MODE_DISABLED";
        public const string StalePong = "STALE_PONG";
        public const string InvalidMessage = "INVALID_MESSAGE";
        public const string InternalError = "INTERNAL_ERROR";

        /// <summary>The search couldn't be queued and was rolled back; nothing is searching. Safe to retry.</summary>
        public const string SearchStartFailed = "SEARCH_START_FAILED";

        // Party errors. None close the connection.
        public const string AlreadyInParty = "ALREADY_IN_PARTY";
        public const string NotInParty = "NOT_IN_PARTY";
        public const string PartyNotFound = "PARTY_NOT_FOUND";
        public const string PartyMemberNotFound = "PARTY_MEMBER_NOT_FOUND";
        public const string PartyFull = "PARTY_FULL";
        public const string PartySearching = "PARTY_SEARCHING";
        public const string NotPartyLeader = "NOT_PARTY_LEADER";
        public const string PartiesNotEnabled = "PARTIES_NOT_ENABLED";
        public const string PartyTooLarge = "PARTY_TOO_LARGE";
        public const string PartyMemberAttributeMissing = "PARTY_MEMBER_ATTRIBUTE_MISSING";
        public const string PartyChanged = "PARTY_CHANGED";
        public const string PartyInviteeUnavailable = "PARTY_INVITEE_UNAVAILABLE";
        public const string PartyInviteNotFound = "PARTY_INVITE_NOT_FOUND";

        /// <summary>
        /// SDK-local, never sent by the server: PROTOCOL.md section 6's codes are all in-band
        /// ERROR messages that presuppose a connection already exists. This one covers the gap
        /// before that - the ticket fetch or the initial WebSocket handshake itself failing (e.g.
        /// the ticket backend is unreachable) - so callers have a single place (ErrorReceived) to
        /// observe every connection failure, not just ones the server reported. Non-terminal: the
        /// SDK retries it with the same backoff as a dropped connection (see
        /// <see cref="MatchmakingClientOptions.MaxReconnectAttempts"/>).
        /// </summary>
        public const string ConnectionFailed = "CONNECTION_FAILED";

        private static readonly HashSet<string> TerminalCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            BillingAccessDenied,
            TokenQuotaExceeded,
            WaitlistAccessDenied,
            PlayerAttributeRequired
        };

        /// <summary>
        /// True for the four codes the server treats as terminal (it closes the connection after
        /// sending them; the SDK must not auto-reconnect). Unknown codes are treated as
        /// non-terminal, per the spec's "treat unknown codes as non-terminal unless the connection
        /// closes" rule.
        /// </summary>
        public static bool IsTerminal(string code) => code != null && TerminalCodes.Contains(code);

        /// <summary>
        /// Maps the raw wire code to <see cref="PairiquiumErrorCode"/>, defaulting to
        /// <see cref="PairiquiumErrorCode.Unknown"/> for anything this SDK version doesn't
        /// recognise - mirrors the "treat unknown codes as non-terminal" forward-compatibility
        /// rule rather than throwing.
        /// </summary>
        public static PairiquiumErrorCode Parse(string code)
        {
            switch (code)
            {
                case BillingAccessDenied: return PairiquiumErrorCode.BillingAccessDenied;
                case TokenQuotaExceeded: return PairiquiumErrorCode.TokenQuotaExceeded;
                case WaitlistAccessDenied: return PairiquiumErrorCode.WaitlistAccessDenied;
                case PlayerAttributeRequired: return PairiquiumErrorCode.PlayerAttributeRequired;
                case GameModeNotFound: return PairiquiumErrorCode.GameModeNotFound;
                case GameModeDisabled: return PairiquiumErrorCode.GameModeDisabled;
                case StalePong: return PairiquiumErrorCode.StalePong;
                case InvalidMessage: return PairiquiumErrorCode.InvalidMessage;
                case InternalError: return PairiquiumErrorCode.InternalError;
                case SearchStartFailed: return PairiquiumErrorCode.SearchStartFailed;
                case AlreadyInParty: return PairiquiumErrorCode.AlreadyInParty;
                case NotInParty: return PairiquiumErrorCode.NotInParty;
                case PartyNotFound: return PairiquiumErrorCode.PartyNotFound;
                case PartyMemberNotFound: return PairiquiumErrorCode.PartyMemberNotFound;
                case PartyFull: return PairiquiumErrorCode.PartyFull;
                case PartySearching: return PairiquiumErrorCode.PartySearching;
                case NotPartyLeader: return PairiquiumErrorCode.NotPartyLeader;
                case PartiesNotEnabled: return PairiquiumErrorCode.PartiesNotEnabled;
                case PartyTooLarge: return PairiquiumErrorCode.PartyTooLarge;
                case PartyMemberAttributeMissing: return PairiquiumErrorCode.PartyMemberAttributeMissing;
                case PartyChanged: return PairiquiumErrorCode.PartyChanged;
                case PartyInviteeUnavailable: return PairiquiumErrorCode.PartyInviteeUnavailable;
                case PartyInviteNotFound: return PairiquiumErrorCode.PartyInviteNotFound;
                case ConnectionFailed: return PairiquiumErrorCode.ConnectionFailed;
                default: return PairiquiumErrorCode.Unknown;
            }
        }
    }
}
