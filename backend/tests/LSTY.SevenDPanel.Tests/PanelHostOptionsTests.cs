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
            Assert.False(options.Authentication.Enabled);
            Assert.Equal(string.Empty, options.Authentication.Username);
            Assert.Equal(string.Empty, options.Authentication.Password);
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
                Assert.True(options.Authentication.Enabled);
                Assert.Equal("username", options.Authentication.Username);
                Assert.Equal("password", options.Authentication.Password);
                Assert.Equal(TimeSpan.FromMinutes(30), options.Authentication.AccessTokenLifetime);
                Assert.True(options.Authentication.AllowInsecureHttp);
                Assert.True(File.Exists(path));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Config_file_can_define_host_and_authentication()
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(
                path,
                "{\"Port\":19090,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Authentication\":{" +
                "\"Enabled\":true,\"Username\":\" admin \",\"Password\":\"pass:word\"," +
                "\"AccessTokenLifetimeMinutes\":45,\"AllowInsecureHttp\":true}}");

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path);
                Assert.Equal("http://127.0.0.1:19090/", options.Url);
                Assert.True(options.Authentication.Enabled);
                Assert.Equal("admin", options.Authentication.Username);
                Assert.Equal("pass:word", options.Authentication.Password);
                Assert.Equal(TimeSpan.FromMinutes(45), options.Authentication.AccessTokenLifetime);
                Assert.True(options.Authentication.AllowInsecureHttp);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("", "password", 30)]
        [InlineData("admin", "", 30)]
        [InlineData("admin", "password", 4)]
        [InlineData("admin", "password", 1441)]
        public void Invalid_authentication_is_disabled_without_replacing_host_binding(
            string username,
            string password,
            int lifetimeMinutes)
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(
                path,
                "{\"Port\":19091,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Authentication\":{" +
                "\"Enabled\":true,\"Username\":" + JsonConvert.SerializeObject(username) + "," +
                "\"Password\":" + JsonConvert.SerializeObject(password) + "," +
                "\"AccessTokenLifetimeMinutes\":" + lifetimeMinutes + ",\"AllowInsecureHttp\":true}}");
            string? message = null;

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path, value => message = value);

                Assert.Equal("http://127.0.0.1:19091/", options.Url);
                Assert.False(options.Authentication.Enabled);
                Assert.Equal(string.Empty, options.Authentication.Username);
                Assert.Equal(string.Empty, options.Authentication.Password);
                Assert.Contains("authentication", message, StringComparison.OrdinalIgnoreCase);
                if (username.Length > 0) Assert.DoesNotContain(username, message ?? string.Empty);
                if (password.Length > 0) Assert.DoesNotContain(password, message ?? string.Empty);
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

            Assert.NotNull(example);
            Assert.Equal(defaults.Port, example.Port);
            Assert.Equal(defaults.BindAddress, example.BindAddress);
            Assert.Equal(defaults.Scheme, example.Scheme);
            Assert.NotNull(example.Authentication);
            Assert.NotNull(defaults.Authentication);
            Assert.Equal(defaults.Authentication.Enabled, example.Authentication.Enabled);
            Assert.Equal(defaults.Authentication.Username, example.Authentication.Username);
            Assert.Equal(defaults.Authentication.Password, example.Authentication.Password);
            Assert.Equal(
                defaults.Authentication.AccessTokenLifetimeMinutes,
                example.Authentication.AccessTokenLifetimeMinutes);
            Assert.Equal(defaults.Authentication.AllowInsecureHttp, example.Authentication.AllowInsecureHttp);
        }
    }
}
