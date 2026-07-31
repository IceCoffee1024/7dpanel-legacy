using System;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ChatCommandTestingOptionsTests
    {
        [Fact]
        public void Disabled_options_never_enable_side_effects()
        {
            var options = PanelChatCommandTestingOptions.FromBinding(
                false,
                "  player-id  ",
                true,
                true);

            Assert.False(options.Enabled);
            Assert.Equal("player-id", options.TestPlayerId);
            Assert.False(options.AllowTeleport);
            Assert.False(options.AllowRewardDelivery);
        }

        [Fact]
        public void Enabled_options_require_a_stable_player_identifier()
        {
            Assert.Throws<ArgumentException>(() =>
                PanelChatCommandTestingOptions.FromBinding(true, " ", false, false));
        }

        [Fact]
        public void Disabled_default_has_no_test_player()
        {
            Assert.False(PanelChatCommandTestingOptions.Disabled.Enabled);
            Assert.Null(PanelChatCommandTestingOptions.Disabled.TestPlayerId);
        }
    }
}
