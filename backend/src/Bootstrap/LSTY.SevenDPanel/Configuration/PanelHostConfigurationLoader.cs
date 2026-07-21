using System;
using System.IO;
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
                return PanelHostOptions.FromBinding(
                    config.Port,
                    config.BindAddress,
                    config.Scheme,
                    authentication);
            }
            catch (Exception ex)
            {
                log?.Invoke("Invalid 7DPanel configuration; using safe defaults: " + ex.Message);
                return CreateDefaultOptions();
            }
        }

        private static PanelHostOptions CreateDefaultOptions()
        {
            return PanelHostOptions.FromBinding(
                PanelHostOptions.DefaultPort,
                PanelHostOptions.DefaultBindAddress,
                PanelHostOptions.DefaultScheme);
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
