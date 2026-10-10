namespace Pairiquium.Messages
{
    /// <summary>
    /// Typed form of <see cref="ErrorData.Code"/> (PROTOCOL.md section 6), for switch/if-based
    /// handling instead of string comparison. <see cref="Unknown"/> covers both error codes this
    /// version of the SDK doesn't recognise (PROTOCOL.md's versioning rules allow new codes in
    /// additive releases) and anything else unexpected - check <see cref="ErrorData.Code"/> (the
    /// raw string) if you need to handle or log a code this enum has no member for.
    /// </summary>
    public enum PairiquiumErrorCode
    {
        Unknown = 0,
        BillingAccessDenied,
        TokenQuotaExceeded,
        WaitlistAccessDenied,
        PlayerAttributeRequired,
        GameModeNotFound,
        GameModeDisabled,
        StalePong,
        InvalidMessage,
        InternalError,
        ConnectionFailed
    }
}
