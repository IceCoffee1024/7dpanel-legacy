using System;
using System.IO;
using LSTY.SevenDPanel.Configuration;
using LSTY.SevenDPanel.Hosting;
using Newtonsoft.Json;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PanelHostOptionsTests
    {
        [Fact]
        public void Url_is_normalized_with_trailing_slash()
        {
            var options = new PanelHostOptions("http://127.0.0.1:18080");
            Assert.Equal("http://127.0.0.1:18080/", options.Url);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-url")]
        [InlineData("ftp://127.0.0.1/")]
        public void Invalid_url_is_rejected(string url)
        {
            Assert.Throws<ArgumentException>(() => new PanelHostOptions(url));
        }

        [Fact]
        public void Config_file_is_loaded_and_default_is_created_when_missing()
        {
            var directory = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "config.json");

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path);

                Assert.Equal("http://*:18080/", options.Url);
                Assert.True(File.Exists(path));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Config_file_can_define_port_bind_address_and_scheme()
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"Port\":19090,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"}");

            try
            {
                Assert.Equal("http://127.0.0.1:19090/", PanelHostConfigurationLoader.FromConfigFile(path).Url);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Config_example_matches_runtime_defaults()
        {
            var examplePath = Path.Combine(AppContext.BaseDirectory, "config.example.json");
            var example = JsonConvert.DeserializeObject<PanelHostConfig>(File.ReadAllText(examplePath));
            var defaults = PanelHostConfig.CreateDefault();

            Assert.Equal(defaults.Port, example.Port);
            Assert.Equal(defaults.BindAddress, example.BindAddress);
            Assert.Equal(defaults.Scheme, example.Scheme);
        }
    }
}
