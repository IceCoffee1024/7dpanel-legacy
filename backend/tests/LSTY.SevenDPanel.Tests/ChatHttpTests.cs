using System;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Application.Chat;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ChatHttpTests
    {
        [Fact]
        public void Chat_controller_exposes_the_approved_owner_only_routes()
        {
            var type = typeof(ChatController);
            Assert.Equal("Owner", type.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            Assert.Equal("api/v1/chat", type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix);

            var routes = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(method => method.GetCustomAttribute<RouteAttribute>()?.Template)
                .Where(route => route != null)
                .ToArray();

            Assert.Equal(14, routes.Length);
            Assert.Contains("messages/recent", routes);
            Assert.Contains("messages", routes);
            Assert.Contains("messages/global", routes);
            Assert.Contains("messages/private", routes);
            Assert.Contains("settings", routes);
            Assert.Contains("colored/settings", routes);
            Assert.Contains("colored/profiles", routes);
            Assert.Contains("colored/profiles/{crossplatformId}", routes);
        }

        [Fact]
        public void History_cursor_round_trips_only_with_the_same_filters()
        {
            var keyset = new ChatHistoryKeyset(
                new DateTimeOffset(2026, 7, 26, 1, 2, 3, TimeSpan.Zero),
                42);
            var filters = new ChatHistoryCursorFilters(
                "EOS_1", "Alice", ChatChannel.Global, ChatSourceKind.Player,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), null);
            var encoded = ChatHistoryCursorCodec.Encode(keyset, filters);

            Assert.True(ChatHistoryCursorCodec.TryDecode(encoded, filters, out var decoded));
            Assert.Equal(keyset.OccurredAtUtc, decoded!.OccurredAtUtc);
            Assert.Equal(keyset.RowId, decoded.RowId);
            Assert.False(ChatHistoryCursorCodec.TryDecode(
                encoded,
                new ChatHistoryCursorFilters("EOS_2", "Alice", ChatChannel.Global,
                    ChatSourceKind.Player, filters.StartUtc, null),
                out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-base64")]
        [InlineData("eyJ2ZXJzaW9uIjo5OX0")]
        public void History_cursor_rejects_invalid_values(string cursor)
        {
            Assert.False(ChatHistoryCursorCodec.TryDecode(
                cursor,
                ChatHistoryCursorFilters.Empty,
                out _));
        }
    }
}
