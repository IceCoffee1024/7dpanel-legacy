using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Configuration
{
    public sealed class PanelHostConfig
    {
        public int Port { get; set; }
        public string? BindAddress { get; set; }
        public string? Scheme { get; set; }

        public static PanelHostConfig CreateDefault()
        {
            return new PanelHostConfig
            {
                Port = PanelHostOptions.DefaultPort,
                BindAddress = PanelHostOptions.DefaultBindAddress,
                Scheme = PanelHostOptions.DefaultScheme
            };
        }
    }
}
