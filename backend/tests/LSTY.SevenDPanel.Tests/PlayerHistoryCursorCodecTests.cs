using System;
using System.Text;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerHistoryCursorCodecTests
    {
        [Fact]
        public void Cursor_round_trips_a_utc_boundary_as_url_safe_base64()
        {
            var expected = new HistoricalPlayersCursor(
                new DateTimeOffset(2026, 7, 25, 12, 30, 0, TimeSpan.Zero),
                "EOS_0002d12af0fe4add9c7de0fbc238d431");

            var encoded = PlayerHistoryCursorCodec.Encode(expected);

            Assert.DoesNotContain("+", encoded);
            Assert.DoesNotContain("/", encoded);
            Assert.True(PlayerHistoryCursorCodec.TryDecode(encoded, out var actual));
            Assert.Equal(expected.FirstObservedAtUtc, actual!.FirstObservedAtUtc);
            Assert.Equal(expected.CrossplatformId, actual.CrossplatformId);
        }

        [Fact]
        public void Cursor_rejects_malformed_input()
        {
            Assert.False(PlayerHistoryCursorCodec.TryDecode("not-a-cursor", out _));
        }

        [Theory]
        [InlineData("{\"version\":\"1\",\"firstObservedUtcMs\":0,\"crossplatformId\":\"EOS_0002d12af0fe4add9c7de0fbc238d431\"}")]
        [InlineData("{\"version\":1,\"firstObservedUtcMs\":0.5,\"crossplatformId\":\"EOS_0002d12af0fe4add9c7de0fbc238d431\"}")]
        [InlineData("{\"version\":999999999999999999999999,\"firstObservedUtcMs\":0,\"crossplatformId\":\"EOS_0002d12af0fe4add9c7de0fbc238d431\"}")]
        [InlineData("{\"version\":1,\"firstObservedUtcMs\":999999999999999999999999,\"crossplatformId\":\"EOS_0002d12af0fe4add9c7de0fbc238d431\"}")]
        public void Cursor_rejects_json_values_with_the_wrong_token_type(string json)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            Assert.False(PlayerHistoryCursorCodec.TryDecode(encoded, out _));
        }
    }
}
