using System;
using System.IO;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class PanelHostOptions
    {
        public const string DefaultUrl = "http://*:18080/";
        public const int DefaultPort = 18080;
        public const string DefaultBindAddress = "0.0.0.0";
        public const string DefaultScheme = "http";
        public const string DefaultGeoIpDatabaseRelativePath = "data/GeoLite2-Country.mmdb";

        public PanelHostOptions(string url)
            : this(
                url,
                false,
                PanelAuthenticationOptions.Disabled,
                PanelOverviewOptions.Disabled,
                PanelPlayerEvidenceOptions.Default,
                RestartScriptOptions.CreateDefault(Path.Combine(AppContext.BaseDirectory, "data")),
                Path.Combine(AppContext.BaseDirectory, "serverconfig.xml"),
                Path.Combine(AppContext.BaseDirectory, DefaultGeoIpDatabaseRelativePath))
        {
        }

        private PanelHostOptions(
            string url,
            bool allowWildcardHost,
            PanelAuthenticationOptions authentication,
            PanelOverviewOptions overview,
            PanelPlayerEvidenceOptions playerEvidence,
            RestartScriptOptions restart,
            string serverConfigurationPath,
            string geoIpDatabasePath)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("The panel URL is required.", nameof(url));
            }

            if (allowWildcardHost &&
                (url.StartsWith("http://*:", StringComparison.OrdinalIgnoreCase) ||
                 url.StartsWith("https://*:", StringComparison.OrdinalIgnoreCase)))
            {
                Url = url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
                Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
                Overview = overview ?? throw new ArgumentNullException(nameof(overview));
                PlayerEvidence = playerEvidence ?? throw new ArgumentNullException(nameof(playerEvidence));
                Restart = restart ?? throw new ArgumentNullException(nameof(restart));
                ServerConfigurationPath = Path.GetFullPath(serverConfigurationPath);
                GeoIpDatabasePath = Path.GetFullPath(geoIpDatabasePath);
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("The panel URL must be an absolute HTTP or HTTPS URL.", nameof(url));
            }

            Url = parsed.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? parsed.AbsoluteUri
                : parsed.AbsoluteUri + "/";
            Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            Overview = overview ?? throw new ArgumentNullException(nameof(overview));
            PlayerEvidence = playerEvidence ?? throw new ArgumentNullException(nameof(playerEvidence));
            Restart = restart ?? throw new ArgumentNullException(nameof(restart));
            ServerConfigurationPath = Path.GetFullPath(serverConfigurationPath);
            GeoIpDatabasePath = Path.GetFullPath(geoIpDatabasePath);
        }

        public string Url { get; }
        public PanelAuthenticationOptions Authentication { get; }
        public PanelOverviewOptions Overview { get; }
        public PanelPlayerEvidenceOptions PlayerEvidence { get; }
        public RestartScriptOptions Restart { get; }
        public string ServerConfigurationPath { get; }
        public string GeoIpDatabasePath { get; }

        public static PanelHostOptions FromBinding(
            int port,
            string? bindAddress,
            string? scheme,
            PanelAuthenticationOptions? authentication = null,
            PanelOverviewOptions? overview = null,
            RestartScriptOptions? restart = null,
            string? serverConfigurationPath = null,
            PanelPlayerEvidenceOptions? playerEvidence = null,
            string? geoIpDatabasePath = null)
        {
            if (port < 1 || port > 65535)
            {
                throw new InvalidDataException("Port must be between 1 and 65535.");
            }

            var normalizedScheme = (scheme ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedScheme.Length == 0) normalizedScheme = DefaultScheme;
            var normalizedAddress = (bindAddress ?? string.Empty).Trim();
            if (normalizedAddress.Length == 0) normalizedAddress = DefaultBindAddress;
            if (normalizedScheme != Uri.UriSchemeHttp && normalizedScheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Scheme must be http or https.");
            }

            var listenerHost = normalizedAddress == "0.0.0.0" ? "*" : normalizedAddress;
            return new PanelHostOptions(
                normalizedScheme + "://" + listenerHost + ":" + port + "/",
                listenerHost == "*",
                authentication ?? PanelAuthenticationOptions.Disabled,
                overview ?? PanelOverviewOptions.Disabled,
                playerEvidence ?? PanelPlayerEvidenceOptions.Default,
                restart ?? RestartScriptOptions.CreateDefault(Path.Combine(AppContext.BaseDirectory, "data")),
                string.IsNullOrWhiteSpace(serverConfigurationPath)
                    ? Path.Combine(AppContext.BaseDirectory, "serverconfig.xml")
                    : serverConfigurationPath!,
                string.IsNullOrWhiteSpace(geoIpDatabasePath)
                    ? Path.Combine(AppContext.BaseDirectory, DefaultGeoIpDatabaseRelativePath)
                    : geoIpDatabasePath!);
        }
    }
}
