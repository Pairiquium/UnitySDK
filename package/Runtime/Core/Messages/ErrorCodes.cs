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
                case ConnectionFailed: return PairiquiumErrorCode.ConnectionFailed;
                default: return PairiquiumErrorCode.Unknown;
            }
        }
    }
}
