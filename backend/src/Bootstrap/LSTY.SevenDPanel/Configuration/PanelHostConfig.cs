using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Configuration
{
    public sealed class PanelHostConfig
    {
        public int Port { get; set; }
        public string? BindAddress { get; set; }
        public string? Scheme { get; set; }
        public PanelAuthenticationConfig? Authentication { get; set; }
        public PanelOverviewConfig? Overview { get; set; }
        public RestartScriptConfig? Restart { get; set; }
        public string? ServerConfigurationPath { get; set; }

        public static PanelHostConfig CreateDefault()
        {
            return new PanelHostConfig
            {
                Port = PanelHostOptions.DefaultPort,
                BindAddress = PanelHostOptions.DefaultBindAddress,
                Scheme = PanelHostOptions.DefaultScheme,
                Authentication = PanelAuthenticationConfig.CreateDefault(),
                Overview = PanelOverviewConfig.CreateDefault(),
                Restart = RestartScriptConfig.CreateDefault(),
                ServerConfigurationPath = "../../serverconfig.xml"
            };
        }
    }

    public sealed class PanelOverviewConfig
    {
        public PublicNetworkConfig? PublicNetwork { get; set; }

        public static PanelOverviewConfig CreateDefault()
        {
            return new PanelOverviewConfig { PublicNetwork = PublicNetworkConfig.CreateDefault() };
        }
    }

    public sealed class PublicNetworkConfig
    {
        public string? Ipv4 { get; set; }
        public string? Ipv6 { get; set; }
        public bool AutoDetectEnabled { get; set; }
        public string? DetectionEndpoint { get; set; }

        public static PublicNetworkConfig CreateDefault()
        {
            return new PublicNetworkConfig { AutoDetectEnabled = false, DetectionEndpoint = null };
        }
    }

    public sealed class RestartScriptConfig
    {
        public string? WindowsScript { get; set; }
        public string? LinuxScript { get; set; }
        public string? WorkingDirectory { get; set; }

        public static RestartScriptConfig CreateDefault()
        {
            return new RestartScriptConfig
            {
                WindowsScript = RestartScriptOptions.DefaultWindowsScript,
                LinuxScript = RestartScriptOptions.DefaultLinuxScript,
                WorkingDirectory = RestartScriptOptions.DefaultWorkingDirectory
            };
        }
    }

    public sealed class PanelAuthenticationConfig
    {
        public bool Enabled { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public int AccessTokenLifetimeMinutes { get; set; }
        public bool AllowInsecureHttp { get; set; }

        public static PanelAuthenticationConfig CreateDefault()
        {
            return new PanelAuthenticationConfig
            {
                Enabled = true,
                Username = "admin",
                Password = "password",
                AccessTokenLifetimeMinutes = PanelAuthenticationOptions.DefaultAccessTokenLifetimeMinutes,
                AllowInsecureHttp = true
            };
        }
    }
}
