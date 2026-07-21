using System;
using System.IO;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class PanelAuthenticationOptions
    {
        public const int DefaultAccessTokenLifetimeMinutes = 30;
        public const int MinimumAccessTokenLifetimeMinutes = 5;
        public const int MaximumAccessTokenLifetimeMinutes = 1440;

        private PanelAuthenticationOptions(
            bool enabled,
            string username,
            string password,
            int accessTokenLifetimeMinutes,
            bool allowInsecureHttp)
        {
            Enabled = enabled;
            Username = username;
            Password = password;
            AccessTokenLifetime = TimeSpan.FromMinutes(accessTokenLifetimeMinutes);
            AllowInsecureHttp = allowInsecureHttp;
        }

        public bool Enabled { get; }
        public string Username { get; }
        public string Password { get; }
        public TimeSpan AccessTokenLifetime { get; }
        public bool AllowInsecureHttp { get; }

        public static PanelAuthenticationOptions Disabled { get; } =
            new PanelAuthenticationOptions(
                false,
                string.Empty,
                string.Empty,
                DefaultAccessTokenLifetimeMinutes,
                false);

        public static PanelAuthenticationOptions FromBinding(
            bool enabled,
            string? username,
            string? password,
            int accessTokenLifetimeMinutes = DefaultAccessTokenLifetimeMinutes,
            bool allowInsecureHttp = false)
        {
            if (!enabled) return Disabled;

            var normalizedUsername = (username ?? string.Empty).Trim();
            if (normalizedUsername.Length == 0)
                throw new InvalidDataException("Authentication username is required when authentication is enabled.");
            if (string.IsNullOrEmpty(password))
                throw new InvalidDataException("Authentication password is required when authentication is enabled.");
            if (accessTokenLifetimeMinutes < MinimumAccessTokenLifetimeMinutes ||
                accessTokenLifetimeMinutes > MaximumAccessTokenLifetimeMinutes)
            {
                throw new InvalidDataException(
                    "Authentication access token lifetime must be between 5 and 1440 minutes.");
            }

            return new PanelAuthenticationOptions(
                true,
                normalizedUsername,
                password!,
                accessTokenLifetimeMinutes,
                allowInsecureHttp);
        }
    }
}
