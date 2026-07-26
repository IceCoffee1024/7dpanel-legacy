using System;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Application.Chat;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ColoredChatRendererTests
    {
        [Fact]
        public void Render_AppliesProfileTemplateAndColors()
        {
            var renderer = new ColoredChatRenderer();
            var rendered = renderer.Render(new ColoredChatRenderRequest(
                "Alice", "steam_1", 42, ChatChannel.Party, ChatSourceKind.Player,
                "hello", Settings(), new ColoredChatProfile
                {
                    CrossplatformId = "steam_1",
                    CustomName = "{playerName} ({entityId}/{chatType})",
                    NameColor = "112233",
                    TextColor = "AABBCC",
                    CreatedAtUtc = Epoch,
                    UpdatedAtUtc = Epoch
                }));

            Assert.Equal("[112233]Alice (42/Party)[-]: [AABBCC]hello[-]", rendered);
        }

        [Fact]
        public void Render_UsesAdminColorAheadOfChannelDefault()
        {
            var renderer = new ColoredChatRenderer();
            var rendered = renderer.Render(new ColoredChatRenderRequest(
                "Admin", null, 1, ChatChannel.Global, ChatSourceKind.Administrator,
                "notice", Settings(), null));

            Assert.Equal("[AA0000]Admin[-]: [AA0000]notice[-]", rendered);
        }

        [Fact]
        public void Render_EscapesPlayerTagsWhenNotAllowedAndPreservesUnknownVariables()
        {
            var renderer = new ColoredChatRenderer();
            var rendered = renderer.Render(new ColoredChatRenderRequest(
                "[FF0000]Alice[-]", "steam_1", 7, ChatChannel.Global, ChatSourceKind.Player,
                "[00FF00]hello[-]", Settings(), new ColoredChatProfile
                {
                    CrossplatformId = "steam_1",
                    CustomName = "{playerName}-{unknown}",
                    CreatedAtUtc = Epoch,
                    UpdatedAtUtc = Epoch
                }));

            Assert.Contains("\\[FF0000]Alice\\[-]-{unknown}", rendered);
            Assert.Contains("\\[00FF00]hello\\[-]", rendered);
        }

        private static ColoredChatSettings Settings() => new ColoredChatSettings
        {
            IsEnabled = true,
            GlobalDefaultColor = "FFFFFF",
            FriendsDefaultColor = "00AA00",
            PartyDefaultColor = "0000AA",
            WhisperDefaultColor = "AAAA00",
            AdminDefaultColor = "AA0000",
            SystemDefaultColor = "777777",
            PlayerColorTagPermission = PlayerColorTagPermission.None
        };

        private static readonly DateTimeOffset Epoch =
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
