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

        public PanelHostOptions(string url) : this(url, false)
        {
        }

        private PanelHostOptions(string url, bool allowWildcardHost)
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
        }

        public string Url { get; }

        public static PanelHostOptions FromBinding(int port, string? bindAddress, string? scheme)
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
                listenerHost == "*");
        }
    }
}
