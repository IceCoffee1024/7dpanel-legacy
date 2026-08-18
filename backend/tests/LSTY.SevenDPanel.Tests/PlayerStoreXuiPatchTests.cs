using System;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Mods;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class PlayerStoreXuiPatchTests
    {
        [Fact]
        public void Store_uri_uses_ServerIP_and_the_configured_panel_binding()
        {
            var created = PlayerStoreXuiPatch.TryCreateStoreUri(
                "http://*:18080/",
                "203.0.113.42",
                out var storeUri);

            Assert.True(created);
            Assert.Equal("http://203.0.113.42:18080/player/store", storeUri!.AbsoluteUri);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0.0.0.0")]
        [InlineData("127.0.0.1")]
        [InlineData("*")]
        [InlineData("not-an-ip")]
        public void Store_uri_rejects_unusable_ServerIP_values(string? serverIp)
        {
            Assert.False(PlayerStoreXuiPatch.TryCreateStoreUri(
                "http://*:18080/",
                serverIp,
                out var storeUri));
            Assert.Null(storeUri);
        }

        [Fact]
        public void Patch_appends_a_native_HTTP_label_below_the_chat_input()
        {
            var storeUri = new Uri("http://203.0.113.42:18080/player/store");
            var document = PlayerStoreXuiPatch.CreateDocument(storeUri);

            var root = Assert.Single(document.Elements("configs"));
            var height = Assert.Single(root.Elements("set"));
            Assert.Equal("/windows/window[@name='chat']/@height", (string?)height.Attribute("xpath"));
            Assert.Equal("314", height.Value);

            var append = Assert.Single(root.Elements("append"));
            Assert.Equal("/windows/window[@name='chat']", (string?)append.Attribute("xpath"));
            var label = Assert.Single(append.Elements("label"));
            Assert.Equal("playerStoreLink", (string?)label.Attribute("name"));
            Assert.Equal("6,-286", (string?)label.Attribute("pos"));
            Assert.Equal("true", (string?)label.Attribute("support_urls"));
            Assert.Equal(
                "[url=http://203.0.113.42:18080/player/store][F0BD57]游戏商店[-][/url]",
                (string?)label.Attribute("text"));
        }

        [Fact]
        public void Invalid_ServerIP_removes_a_stale_generated_patch()
        {
            var root = Path.Combine(Path.GetTempPath(), "7dpanel-xui-" + Guid.NewGuid().ToString("N"));
            var patchPath = Path.Combine(root, PlayerStoreXuiPatch.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
                File.WriteAllText(patchPath, "stale");

                PlayerStoreXuiPatch.Write(
                    root,
                    new PanelHostOptions("http://127.0.0.1:18080"),
                    "0.0.0.0",
                    _ => { });

                Assert.False(File.Exists(patchPath));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Patch_waits_until_Steam_populates_ServerIP()
        {
            var root = Path.Combine(Path.GetTempPath(), "7dpanel-xui-" + Guid.NewGuid().ToString("N"));
            var patchPath = Path.Combine(root, PlayerStoreXuiPatch.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var values = new[] { string.Empty, "127.0.0.1", "203.0.113.42" };
            var index = 0;
            try
            {
                var write = PlayerStoreXuiPatch.WriteWhenServerIpAvailable(
                    root,
                    new PanelHostOptions("http://127.0.0.1:18080"),
                    () => values[Math.Min(index++, values.Length - 1)],
                    _ => { },
                    () => new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));

                while (write.MoveNext()) { }

                Assert.Contains(
                    "http://203.0.113.42:18080/player/store",
                    File.ReadAllText(patchPath));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Configured_ServerIP_overrides_GamePrefs()
        {
            var root = Path.Combine(Path.GetTempPath(), "7dpanel-xui-" + Guid.NewGuid().ToString("N"));
            var patchPath = Path.Combine(root, PlayerStoreXuiPatch.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var options = PanelHostOptions.FromBinding(
                    18080,
                    "0.0.0.0",
                    "http",
                    playerStoreServerIp: "203.0.113.42");
                var write = PlayerStoreXuiPatch.WriteWhenServerIpAvailable(
                    root,
                    options,
                    () => throw new InvalidOperationException("GamePrefs should not be read"),
                    _ => { });

                Assert.False(write.MoveNext());
                Assert.Contains(
                    "http://203.0.113.42:18080/player/store",
                    File.ReadAllText(patchPath));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
