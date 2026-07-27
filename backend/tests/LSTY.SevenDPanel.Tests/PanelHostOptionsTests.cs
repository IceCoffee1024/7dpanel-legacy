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
            Assert.Equal(PanelPlayerEvidenceOptions.DefaultServerId, options.PlayerEvidence.ServerId);
            Assert.Equal(PanelPlayerEvidenceOptions.DefaultTimeZoneId, options.PlayerEvidence.TimeZone.Id);
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
                Assert.Equal("admin", options.Authentication.Username);
                Assert.Equal("password", options.Authentication.Password);
                Assert.Equal(TimeSpan.FromHours(8), options.Authentication.AccessTokenLifetime);
                Assert.True(options.Authentication.AllowInsecureHttp);
                Assert.Equal("local", options.PlayerEvidence.ServerId);
                Assert.Equal("UTC", options.PlayerEvidence.TimeZone.Id);
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
            Assert.Equal(480, defaults.Authentication.AccessTokenLifetimeMinutes);
            Assert.Equal(defaults.Authentication.AllowInsecureHttp, example.Authentication.AllowInsecureHttp);
            Assert.NotNull(example.Overview);
            Assert.NotNull(defaults.Overview);
            Assert.NotNull(example.Overview.PublicNetwork);
            Assert.NotNull(defaults.Overview.PublicNetwork);
            Assert.Equal(defaults.Overview.PublicNetwork.AutoDetectEnabled, example.Overview.PublicNetwork.AutoDetectEnabled);
            Assert.False(defaults.Overview.PublicNetwork.AutoDetectEnabled);
            Assert.Equal(defaults.Overview.PublicNetwork.DetectionEndpoint, example.Overview.PublicNetwork.DetectionEndpoint);
            Assert.NotNull(example.Restart);
            Assert.NotNull(defaults.Restart);
            Assert.Equal(defaults.Restart.WindowsScript, example.Restart.WindowsScript);
            Assert.Equal(defaults.Restart.LinuxScript, example.Restart.LinuxScript);
            Assert.Equal(defaults.Restart.WorkingDirectory, example.Restart.WorkingDirectory);
            Assert.NotNull(example.PlayerEvidence);
            Assert.NotNull(defaults.PlayerEvidence);
            Assert.Equal(defaults.PlayerEvidence.ServerId, example.PlayerEvidence.ServerId);
            Assert.Equal(defaults.PlayerEvidence.TimeZoneId, example.PlayerEvidence.TimeZoneId);
            Assert.Equal("local", defaults.PlayerEvidence.ServerId);
            Assert.Equal("UTC", defaults.PlayerEvidence.TimeZoneId);
        }

        [Fact]
        public void Invalid_player_evidence_time_zone_falls_back_only_that_section()
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path,
                "{\"Port\":19096,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Authentication\":{\"Enabled\":true,\"Username\":\"admin\",\"Password\":\"password\",\"AccessTokenLifetimeMinutes\":30,\"AllowInsecureHttp\":true}," +
                "\"PlayerEvidence\":{\"ServerId\":\"remote-a\",\"TimeZoneId\":\"not/a-real-time-zone\"}}");
            string? message = null;

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path, value => message = value);

                Assert.Equal("http://127.0.0.1:19096/", options.Url);
                Assert.True(options.Authentication.Enabled);
                Assert.Equal("local", options.PlayerEvidence.ServerId);
                Assert.Equal("UTC", options.PlayerEvidence.TimeZone.Id);
                Assert.Contains("player evidence", message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("not/a-real-time-zone", message ?? string.Empty, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Player_evidence_binding_trims_server_and_resolves_installed_time_zone()
        {
            var timeZone = TimeZoneInfo.Local;
            var options = PanelPlayerEvidenceOptions.FromBinding(" remote-a ", timeZone.Id);

            Assert.Equal("remote-a", options.ServerId);
            Assert.Equal(timeZone.Id, options.TimeZone.Id);
            Assert.Equal(PanelPlayerEvidenceOptions.DefaultQueueCapacity, options.QueueCapacity);
            Assert.Equal(PanelPlayerEvidenceOptions.DefaultDrainTimeout, options.DrainTimeout);
            Assert.Equal(PanelPlayerEvidenceOptions.DefaultRetention, options.Retention);
        }

        [Fact]
        public void Relative_restart_paths_are_normalized_under_the_mod_data_directory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path,
                "{\"Port\":19092,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Restart\":{\"WindowsScript\":\"scripts\\\\restart.cmd\",\"LinuxScript\":\"scripts/restart.sh\",\"WorkingDirectory\":\".\"}}");

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path);

                Assert.Equal(Path.Combine(directory, "data", "scripts", "restart.cmd"), options.Restart.WindowsScript);
                Assert.Equal(Path.Combine(directory, "data", "scripts", "restart.sh"), options.Restart.LinuxScript);
                Assert.Equal(Path.Combine(directory, "data"), options.Restart.WorkingDirectory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Theory]
        [InlineData("scripts/restart.ps1")]
        [InlineData("scripts/restart.cmd.bak")]
        public void Windows_restart_script_requires_cmd_extension(string windowsScript)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                RestartScriptOptions.FromBinding(
                    windowsScript,
                    RestartScriptOptions.DefaultLinuxScript,
                    RestartScriptOptions.DefaultWorkingDirectory,
                    Path.GetTempPath()));

            Assert.Equal("Windows restart script must use the .cmd extension.", exception.Message);
            Assert.DoesNotContain(windowsScript, exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Windows_restart_script_accepts_cmd_extension_case_insensitively()
        {
            var options = RestartScriptOptions.FromBinding(
                "scripts/restart.CMD",
                RestartScriptOptions.DefaultLinuxScript,
                RestartScriptOptions.DefaultWorkingDirectory,
                Path.GetTempPath());

            Assert.EndsWith("restart.CMD", options.WindowsScript, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("scripts/restart.bash")]
        [InlineData("scripts/restart.SH")]
        public void Linux_restart_script_requires_case_sensitive_sh_extension(string linuxScript)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                RestartScriptOptions.FromBinding(
                    RestartScriptOptions.DefaultWindowsScript,
                    linuxScript,
                    RestartScriptOptions.DefaultWorkingDirectory,
                    Path.GetTempPath()));

            Assert.Equal("Linux restart script must use the .sh extension.", exception.Message);
            Assert.DoesNotContain(linuxScript, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Restart_script_paths_with_spaces_remain_supported()
        {
            var options = RestartScriptOptions.FromBinding(
                "scripts/restart server.cmd",
                "scripts/restart server.sh",
                ".",
                Path.GetTempPath());

            Assert.EndsWith("restart server.cmd", options.WindowsScript, StringComparison.Ordinal);
            Assert.EndsWith("restart server.sh", options.LinuxScript, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("scripts/%TEMP%.cmd")]
        [InlineData("scripts/re\"start.cmd")]
        [InlineData("scripts/re&start.cmd")]
        [InlineData("scripts/re^start.cmd")]
        public void Windows_restart_script_rejects_shell_sensitive_characters(
            string windowsScript)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                RestartScriptOptions.FromBinding(
                    windowsScript,
                    RestartScriptOptions.DefaultLinuxScript,
                    RestartScriptOptions.DefaultWorkingDirectory,
                    Path.GetTempPath()));

            Assert.Equal(
                "Windows restart script contains unsupported characters.",
                exception.Message);
            Assert.DoesNotContain(windowsScript, exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("scripts/re\"start.sh")]
        [InlineData("scripts/re'start.sh")]
        [InlineData("scripts/re$HOME.sh")]
        [InlineData("scripts/re`id`.sh")]
        public void Linux_restart_script_rejects_shell_sensitive_characters(
            string linuxScript)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                RestartScriptOptions.FromBinding(
                    RestartScriptOptions.DefaultWindowsScript,
                    linuxScript,
                    RestartScriptOptions.DefaultWorkingDirectory,
                    Path.GetTempPath()));

            Assert.Equal(
                "Linux restart script contains unsupported characters.",
                exception.Message);
            Assert.DoesNotContain(linuxScript, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Invalid_overview_and_restart_sections_fall_back_without_replacing_host_or_authentication()
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path,
                "{\"Port\":19093,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Authentication\":{\"Enabled\":true,\"Username\":\"admin\",\"Password\":\"password\",\"AccessTokenLifetimeMinutes\":30,\"AllowInsecureHttp\":true}," +
                "\"Overview\":{\"PublicNetwork\":{\"Ipv4\":\"not-an-ip\",\"AutoDetectEnabled\":true,\"DetectionEndpoint\":\"http://example.test/ip\"}}," +
                "\"Restart\":{\"WindowsScript\":\"\",\"LinuxScript\":\"\",\"WorkingDirectory\":\"\"}}");

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path);

                Assert.Equal("http://127.0.0.1:19093/", options.Url);
                Assert.True(options.Authentication.Enabled);
                Assert.False(options.Overview.PublicNetwork.AutoDetectEnabled);
                Assert.Null(options.Overview.PublicNetwork.DetectionEndpoint);
                Assert.EndsWith(Path.Combine("data", "scripts", "restart-server.cmd"), options.Restart.WindowsScript);
                Assert.EndsWith(Path.Combine("data", "scripts", "restart-server.sh"), options.Restart.LinuxScript);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Restart_path_escape_falls_back_only_the_restart_section()
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path,
                "{\"Port\":19094,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Authentication\":{\"Enabled\":true,\"Username\":\"admin\",\"Password\":\"password\",\"AccessTokenLifetimeMinutes\":30,\"AllowInsecureHttp\":true}," +
                "\"Restart\":{\"WindowsScript\":\"../../restart-server.cmd\",\"LinuxScript\":\"scripts/restart-server.sh\",\"WorkingDirectory\":\".\"}}");

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path);

                Assert.Equal("http://127.0.0.1:19094/", options.Url);
                Assert.True(options.Authentication.Enabled);
                Assert.EndsWith(Path.Combine("data", "scripts", "restart-server.cmd"), options.Restart.WindowsScript);
                Assert.EndsWith(Path.Combine("data", "scripts", "restart-server.sh"), options.Restart.LinuxScript);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Restart_path_with_nul_falls_back_only_the_restart_section()
        {
            var path = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path,
                "{\"Port\":19095,\"BindAddress\":\"127.0.0.1\",\"Scheme\":\"http\"," +
                "\"Authentication\":{\"Enabled\":true,\"Username\":\"admin\",\"Password\":\"password\",\"AccessTokenLifetimeMinutes\":30,\"AllowInsecureHttp\":true}," +
                "\"Restart\":{\"WindowsScript\":\"scripts\\u0000restart.cmd\",\"LinuxScript\":\"scripts/restart-server.sh\",\"WorkingDirectory\":\".\"}}");

            try
            {
                var options = PanelHostConfigurationLoader.FromConfigFile(path);

                Assert.Equal("http://127.0.0.1:19095/", options.Url);
                Assert.True(options.Authentication.Enabled);
                Assert.EndsWith(Path.Combine("data", "scripts", "restart-server.cmd"), options.Restart.WindowsScript);
                Assert.EndsWith(Path.Combine("data", "scripts", "restart-server.sh"), options.Restart.LinuxScript);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Absolute_restart_paths_outside_the_data_directory_are_rejected_as_configuration_errors()
        {
            var directory = Path.Combine(Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N"));
            var dataDirectory = Path.Combine(directory, "data");
            var outsideScript = Path.Combine(directory, "restart-server.cmd");

            Assert.Throws<InvalidDataException>(() => RestartScriptOptions.FromBinding(
                outsideScript,
                RestartScriptOptions.DefaultLinuxScript,
                RestartScriptOptions.DefaultWorkingDirectory,
                dataDirectory));
        }

        [Theory]
        [InlineData(5)]
        [InlineData(1440)]
        public void Authentication_lifetime_accepts_the_existing_configuration_boundaries(
            int lifetimeMinutes)
        {
            var options = PanelAuthenticationOptions.FromBinding(
                enabled: true,
                username: "admin",
                password: "password",
                accessTokenLifetimeMinutes: lifetimeMinutes,
                allowInsecureHttp: true);

            Assert.Equal(TimeSpan.FromMinutes(lifetimeMinutes), options.AccessTokenLifetime);
        }
    }
}
