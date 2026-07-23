using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Configuration
{
    public sealed class PanelHostConfig
    {
        public int Port { get; set; }
        public string? BindAddress { get; set; }
        public string? Scheme { get; set; }
        public PanelAuthenticationConfig? Authentication { get; set; }

        public static PanelHostConfig CreateDefault()
        {
            return new PanelHostConfig
            {
                Port = PanelHostOptions.DefaultPort,
                BindAddress = PanelHostOptions.DefaultBindAddress,
                Scheme = PanelHostOptions.DefaultScheme,
                Authentication = PanelAuthenticationConfig.CreateDefault()
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
