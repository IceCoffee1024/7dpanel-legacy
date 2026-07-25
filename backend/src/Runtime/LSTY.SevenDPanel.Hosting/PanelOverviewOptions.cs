using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class PanelOverviewOptions
    {
        private PanelOverviewOptions(string? ipv4, string? ipv6, bool autoDetectEnabled, string? detectionEndpoint)
        {
            Ipv4 = ipv4;
            Ipv6 = ipv6;
            AutoDetectEnabled = autoDetectEnabled;
            DetectionEndpoint = detectionEndpoint;
        }

        public string? Ipv4 { get; }
        public string? Ipv6 { get; }
        public bool AutoDetectEnabled { get; }
        public string? DetectionEndpoint { get; }
        public PanelOverviewOptions PublicNetwork => this;

        public static PanelOverviewOptions Disabled { get; } = new PanelOverviewOptions(null, null, false, null);

        public static PanelOverviewOptions FromBinding(
            string? ipv4,
            string? ipv6,
            bool autoDetectEnabled,
            string? detectionEndpoint)
        {
            var normalizedIpv4 = NormalizeAddress(ipv4, AddressFamily.InterNetwork, "IPv4");
            var normalizedIpv6 = NormalizeAddress(ipv6, AddressFamily.InterNetworkV6, "IPv6");
            var normalizedEndpoint = NormalizeEndpoint(detectionEndpoint);
            if (autoDetectEnabled && normalizedEndpoint == null)
                throw new InvalidDataException("A HTTPS detection endpoint is required when public network auto detection is enabled.");

            return new PanelOverviewOptions(normalizedIpv4, normalizedIpv6, autoDetectEnabled, normalizedEndpoint);
        }

        private static string? NormalizeAddress(string? value, AddressFamily family, string label)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) return null;
            if (!IPAddress.TryParse(normalized, out var address) || address.AddressFamily != family)
                throw new InvalidDataException(label + " must be a valid " + label + " address.");
            return address.ToString();
        }

        private static string? NormalizeEndpoint(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) return null;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Public network detection endpoint must be an absolute HTTPS URL.");
            return endpoint.AbsoluteUri;
        }
    }
}
