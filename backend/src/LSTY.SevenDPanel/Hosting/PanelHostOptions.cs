using System;
using System.IO;
using Newtonsoft.Json;

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

        public static PanelHostOptions FromMod(Mod modInstance, Action<string> log = null)
        {
            if (modInstance == null || string.IsNullOrWhiteSpace(modInstance.Path))
            {
                return new PanelHostOptions(DefaultUrl, true);
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

        public static PanelHostOptions FromConfigFile(string configPath, Action<string> log = null)
        {
            try
            {
                PanelHostConfig config;
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

                return FromConfig(config);
            }
            catch (Exception ex)
            {
                log?.Invoke("Invalid 7DPanel configuration; using safe defaults: " + ex.Message);
                return new PanelHostOptions(DefaultUrl, true);
            }
        }

        private static PanelHostOptions FromConfig(PanelHostConfig config)
        {
            if (config.Port < 1 || config.Port > 65535)
            {
                throw new InvalidDataException("Port must be between 1 and 65535.");
            }

            var scheme = string.IsNullOrWhiteSpace(config.Scheme) ? DefaultScheme : config.Scheme.Trim().ToLowerInvariant();
            var bindAddress = string.IsNullOrWhiteSpace(config.BindAddress) ? DefaultBindAddress : config.BindAddress.Trim();
            if (scheme != Uri.UriSchemeHttp && scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Scheme must be http or https.");
            }

            var listenerHost = bindAddress == "0.0.0.0" ? "*" : bindAddress;
            return new PanelHostOptions(
                scheme + "://" + listenerHost + ":" + config.Port + "/",
                listenerHost == "*");
        }
    }

    public sealed class PanelHostConfig
    {
        public int Port { get; set; }
        public string BindAddress { get; set; }
        public string Scheme { get; set; }

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
