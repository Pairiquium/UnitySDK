namespace Pairiquium
{
    public enum MatchmakingConnectionState
    {
        Idle,
        Connecting,
        Searching,
        Reconnecting,
        Closed,
        Faulted,

        /// <summary>
        /// Connected via <c>ConnectAsync</c> and not searching: the state to form or manage a party
        /// in. Appended last so existing values keep their numbers.
        /// </summary>
        Connected
    }
}
