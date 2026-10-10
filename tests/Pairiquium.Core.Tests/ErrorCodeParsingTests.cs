using NUnit.Framework;
using Pairiquium.Messages;

namespace Pairiquium.Core.Tests
{
    [TestFixture]
    public class ErrorCodeParsingTests
    {
        [TestCase(ErrorCodes.BillingAccessDenied, PairiquiumErrorCode.BillingAccessDenied)]
        [TestCase(ErrorCodes.TokenQuotaExceeded, PairiquiumErrorCode.TokenQuotaExceeded)]
        [TestCase(ErrorCodes.WaitlistAccessDenied, PairiquiumErrorCode.WaitlistAccessDenied)]
        [TestCase(ErrorCodes.PlayerAttributeRequired, PairiquiumErrorCode.PlayerAttributeRequired)]
        [TestCase(ErrorCodes.GameModeNotFound, PairiquiumErrorCode.GameModeNotFound)]
        [TestCase(ErrorCodes.GameModeDisabled, PairiquiumErrorCode.GameModeDisabled)]
        [TestCase(ErrorCodes.StalePong, PairiquiumErrorCode.StalePong)]
        [TestCase(ErrorCodes.InvalidMessage, PairiquiumErrorCode.InvalidMessage)]
        [TestCase(ErrorCodes.InternalError, PairiquiumErrorCode.InternalError)]
        public void Parse_KnownCode_MapsToMatchingEnumValue(string code, PairiquiumErrorCode expected)
        {
            Assert.That(ErrorCodes.Parse(code), Is.EqualTo(expected));
        }

        [TestCase("SOME_FUTURE_CODE")]
        [TestCase(null)]
        [TestCase("")]
        public void Parse_UnknownOrMissingCode_MapsToUnknown(string code)
        {
            Assert.That(ErrorCodes.Parse(code), Is.EqualTo(PairiquiumErrorCode.Unknown));
        }

        [Test]
        public void ErrorData_ExposesErrorCode_AlongsideRawCodeString()
        {
            const string json = "{\"action\":\"ERROR\",\"data\":{\"code\":\"GAME_MODE_NOT_FOUND\",\"message\":\"no such mode\"}}";
            var message = Newtonsoft.Json.JsonConvert.DeserializeObject<ErrorMessage>(json);

            Assert.That(message.Data.Code, Is.EqualTo("GAME_MODE_NOT_FOUND"));
            Assert.That(message.Data.ErrorCode, Is.EqualTo(PairiquiumErrorCode.GameModeNotFound));
        }
    }
}
