using System;
using System.IO;
using System.Security;
using LSTY.SevenDPanel.Hosting;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Configuration
{
    public static class PanelHostConfigurationLoader
    {
        public static PanelHostOptions FromMod(Mod? modInstance, Action<string>? log = null)
        {
            if (modInstance == null || string.IsNullOrWhiteSpace(modInstance.Path))
            {
                return CreateDefaultOptions();
            }

            var modDirectory = modInstance.Path;
            try
            {
                Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
            }
            catch (Exception ex)
            {
                log?.Invoke("Could not create the 7DPanel data directory: " + ex.Message);
            }

            return FromConfigFile(Path.Combine(modDirectory, "config.json"), log);
        }

        public static PanelHostOptions FromConfigFile(string configPath, Action<string>? log = null)
        {
            try
            {
                PanelHostConfig? config;
                if (File.Exists(configPath))
                {
                    config = JsonConvert.DeserializeObject<PanelHostConfig>(File.ReadAllText(configPath));
                }
                else
                {
                    config = PanelHostConfig.CreateDefault();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                    log?.Invoke("Created default configuration at " + configPath);
                }

                if (config == null)
                {
                    throw new InvalidDataException("The configuration document is empty.");
                }

                var authentication = CreateAuthenticationOptions(config.Authentication, log);
                var dataDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "data");
                var overview = CreateOverviewOptions(config.Overview, log);
                var playerEvidence = CreatePlayerEvidenceOptions(config.PlayerEvidence, log);
                var chatCommandTesting = CreateChatCommandTestingOptions(config.ChatCommandTesting, log);
                var restart = CreateRestartScriptOptions(config.Restart, dataDirectory, log);
                var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
                var serverConfigurationPath = Path.GetFullPath(Path.Combine(
                    configDirectory,
                    string.IsNullOrWhiteSpace(config.ServerConfigurationPath)
                        ? "../../serverconfig.xml"
                        : config.ServerConfigurationPath));
                var geoIpDatabasePath = CreateGeoIpDatabasePath(
                    configDirectory,
                    config.GeoIpDatabasePath,
                    log);
                return PanelHostOptions.FromBinding(
                    config.Port,
                    config.BindAddress,
                    config.Scheme,
                    authentication,
                    overview,
                    restart,
                    serverConfigurationPath,
                    playerEvidence,
                    geoIpDatabasePath,
                    chatCommandTesting);
            }
            catch (Exception ex)
            {
                log?.Invoke("Invalid 7DPanel configuration; using safe defaults: " + ex.Message);
                return CreateDefaultOptions();
            }
        }

        private static PanelHostOptions CreateDefaultOptions()
        {
            var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
            return PanelHostOptions.FromBinding(
                PanelHostOptions.DefaultPort,
                PanelHostOptions.DefaultBindAddress,
                PanelHostOptions.DefaultScheme,
                restart: RestartScriptOptions.CreateDefault(dataDirectory),
                serverConfigurationPath: Path.Combine(AppContext.BaseDirectory, "serverconfig.xml"));
        }

        private static string CreateGeoIpDatabasePath(
            string configDirectory,
            string? configuredPath,
            Action<string>? log)
        {
            try
            {
                return Path.GetFullPath(Path.Combine(
                    configDirectory,
                    string.IsNullOrWhiteSpace(configuredPath)
                        ? PanelHostOptions.DefaultGeoIpDatabaseRelativePath
                        : configuredPath));
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException ||
                ex is SecurityException)
            {
                log?.Invoke("Invalid 7DPanel GeoIP database path; using the default path.");
                return Path.GetFullPath(Path.Combine(
                    configDirectory,
                    PanelHostOptions.DefaultGeoIpDatabaseRelativePath));
            }
        }

        private static PanelPlayerEvidenceOptions CreatePlayerEvidenceOptions(
            PanelPlayerEvidenceConfig? config,
            Action<string>? log)
        {
            config ??= PanelPlayerEvidenceConfig.CreateDefault();
            try
            {
                return PanelPlayerEvidenceOptions.FromBinding(
                    config.ServerId,
                    config.TimeZoneId);
            }
            catch (InvalidDataException ex)
            {
                log?.Invoke("Invalid 7DPanel player evidence configuration; using safe defaults: " + ex.Message);
                return PanelPlayerEvidenceOptions.Default;
            }
        }

        private static PanelChatCommandTestingOptions CreateChatCommandTestingOptions(
            ChatCommandTestingConfig? config,
            Action<string>? log)
        {
            config ??= ChatCommandTestingConfig.CreateDefault();
            try
            {
                return PanelChatCommandTestingOptions.FromBinding(
                    config.Enabled,
                    config.TestPlayerId,
                    config.AllowTeleport,
                    config.AllowRewardDelivery);
            }
            catch (ArgumentException ex)
            {
                log?.Invoke("Invalid chat-command testing configuration; testing disabled: " + ex.Message);
                return PanelChatCommandTestingOptions.Disabled;
            }
        }

        private static PanelOverviewOptions CreateOverviewOptions(PanelOverviewConfig? config, Action<string>? log)
        {
            var network = config?.PublicNetwork ?? PublicNetworkConfig.CreateDefault();
            try
            {
                return PanelOverviewOptions.FromBinding(
                    network.Ipv4,
                    network.Ipv6,
                    network.AutoDetectEnabled,
                    network.DetectionEndpoint);
            }
            catch (InvalidDataException ex)
            {
                log?.Invoke("Invalid 7DPanel overview configuration; public network detection disabled: " + ex.Message);
                return PanelOverviewOptions.Disabled;
            }
        }

        private static RestartScriptOptions CreateRestartScriptOptions(
            RestartScriptConfig? config,
            string dataDirectory,
            Action<string>? log)
        {
            config ??= RestartScriptConfig.CreateDefault();
            try
            {
                return RestartScriptOptions.FromBinding(
                    config.WindowsScript,
                    config.LinuxScript,
                    config.WorkingDirectory,
                    dataDirectory);
            }
            catch (InvalidDataException ex)
            {
                log?.Invoke("Invalid 7DPanel restart configuration; using safe defaults: " + ex.Message);
                return RestartScriptOptions.CreateDefault(dataDirectory);
            }
        }

        private static PanelAuthenticationOptions CreateAuthenticationOptions(
            PanelAuthenticationConfig? config,
            Action<string>? log)
        {
            config ??= PanelAuthenticationConfig.CreateDefault();
            try
            {
                return PanelAuthenticationOptions.FromBinding(
                    config.Enabled,
                    config.Username,
                    config.Password,
                    config.AccessTokenLifetimeMinutes,
                    config.AllowInsecureHttp);
            }
            catch (InvalidDataException ex)
            {
                log?.Invoke("Invalid 7DPanel authentication configuration; authentication disabled: " + ex.Message);
                return PanelAuthenticationOptions.Disabled;
            }
        }
    }
}
