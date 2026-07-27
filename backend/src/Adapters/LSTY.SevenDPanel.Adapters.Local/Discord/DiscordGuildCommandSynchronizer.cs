using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Discord;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public enum DiscordGuildCommandSynchronizationDisposition
    {
        Succeeded,
        Retryable,
        Rejected,
        ResultUnknown
    }

    public sealed class DiscordGuildCommandSynchronizationResult
    {
        private DiscordGuildCommandSynchronizationResult(
            DiscordGuildCommandSynchronizationDisposition disposition,
            string? errorCode,
            TimeSpan? retryAfter)
        {
            Disposition = disposition;
            ErrorCode = errorCode;
            RetryAfter = retryAfter;
        }

        public DiscordGuildCommandSynchronizationDisposition Disposition { get; }
        public string? ErrorCode { get; }
        public TimeSpan? RetryAfter { get; }

        public static DiscordGuildCommandSynchronizationResult Succeeded() =>
            new DiscordGuildCommandSynchronizationResult(
                DiscordGuildCommandSynchronizationDisposition.Succeeded,
                null,
                null);

        public static DiscordGuildCommandSynchronizationResult Retryable(
            string errorCode,
            TimeSpan? retryAfter) =>
            new DiscordGuildCommandSynchronizationResult(
                DiscordGuildCommandSynchronizationDisposition.Retryable,
                RequireErrorCode(errorCode),
                retryAfter);

        public static DiscordGuildCommandSynchronizationResult Rejected(string errorCode) =>
            new DiscordGuildCommandSynchronizationResult(
                DiscordGuildCommandSynchronizationDisposition.Rejected,
                RequireErrorCode(errorCode),
                null);

        public static DiscordGuildCommandSynchronizationResult ResultUnknown() =>
            new DiscordGuildCommandSynchronizationResult(
                DiscordGuildCommandSynchronizationDisposition.ResultUnknown,
                "discord_guild_command_sync_result_unknown",
                null);

        public override string ToString() =>
            $"DiscordGuildCommandSynchronizationResult {{ Disposition = {Disposition}, ErrorCode = {ErrorCode}, RetryAfter = {RetryAfter} }}";

        private static string RequireErrorCode(string errorCode) =>
            string.IsNullOrWhiteSpace(errorCode)
                ? throw new ArgumentException("An error code is required.", nameof(errorCode))
                : errorCode;
    }

    public sealed class DiscordGuildCommandSynchronizationRequest
    {
        public DiscordGuildCommandSynchronizationRequest(
            string applicationId,
            string guildId,
            string botToken,
            DiscordProxyConfiguration? proxy)
        {
            ApplicationId = applicationId;
            GuildId = guildId;
            BotToken = botToken;
            Proxy = proxy;
        }

        public string ApplicationId { get; }
        public string GuildId { get; }
        public string BotToken { get; }
        public DiscordProxyConfiguration? Proxy { get; }

        public override string ToString() =>
            $"DiscordGuildCommandSynchronizationRequest {{ ApplicationId = {ApplicationId}, GuildId = {GuildId}, BotToken = [REDACTED], Proxy = {Proxy} }}";
    }

    public sealed class DiscordGuildCommandSynchronizer : IDisposable
    {
        private static readonly Uri ApiBaseAddress = new Uri("https://discord.com/api/v10/");
        private readonly HttpClient? fixedClient;
        private bool disposed;

        public DiscordGuildCommandSynchronizer() { }

        public DiscordGuildCommandSynchronizer(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            fixedClient = new HttpClient(handler, false);
        }

        public async Task<DiscordGuildCommandSynchronizationResult> SynchronizeAsync(
            DiscordGuildCommandSynchronizationRequest request,
            CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiscordGuildCommandSynchronizer));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!IsValid(request))
                return DiscordGuildCommandSynchronizationResult.Rejected(
                    "discord_guild_command_sync_invalid");

            HttpRequestMessage message;
            try
            {
                message = CreateMessage(request);
            }
            catch
            {
                return DiscordGuildCommandSynchronizationResult.Rejected(
                    "discord_guild_command_sync_invalid");
            }

            using (message)
            {
                if (fixedClient != null)
                    return await SendCoreAsync(fixedClient, message, cancellationToken).ConfigureAwait(false);

                using var handler = CreateHandler(request.Proxy);
                using var client = new HttpClient(handler);
                return await SendCoreAsync(client, message, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            fixedClient?.Dispose();
        }

        private static bool IsValid(DiscordGuildCommandSynchronizationRequest request) =>
            IsSnowflake(request.ApplicationId) &&
            IsSnowflake(request.GuildId) &&
            !string.IsNullOrWhiteSpace(request.BotToken);

        private static bool IsSnowflake(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value!.Length <= 20 &&
            value.All(character => character >= '0' && character <= '9');

        private static HttpRequestMessage CreateMessage(
            DiscordGuildCommandSynchronizationRequest request)
        {
            var endpoint = new Uri(
                ApiBaseAddress,
                "applications/" + request.ApplicationId + "/guilds/" + request.GuildId + "/commands");
            var message = new HttpRequestMessage(HttpMethod.Put, endpoint)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(FixedCommands()),
                    Encoding.UTF8,
                    "application/json")
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bot", request.BotToken);
            return message;
        }

        private static object[] FixedCommands()
        {
            var names = new[]
            {
                DiscordSlashCommandNames.Bind,
                DiscordSlashCommandNames.Status,
                DiscordSlashCommandNames.Players
            };
            if (names.Any(commandName => !DiscordSlashCommandNames.IsAllowed(commandName)))
                throw new InvalidOperationException("discord_guild_command_sync_invalid");

            return new object[]
            {
                new
                {
                    name = DiscordSlashCommandNames.Bind,
                    description = "Link your panel account.",
                    type = 1,
                    options = new[]
                    {
                        new
                        {
                            name = "code",
                            description = "The binding code from 7DPanel.",
                            type = 3,
                            required = true
                        }
                    }
                },
                new
                {
                    name = DiscordSlashCommandNames.Status,
                    description = "Show the server status.",
                    type = 1
                },
                new
                {
                    name = DiscordSlashCommandNames.Players,
                    description = "Show online players.",
                    type = 1
                }
            };
        }

        private static HttpClientHandler CreateHandler(DiscordProxyConfiguration? proxy)
        {
            var handler = new HttpClientHandler();
            if (proxy == null) return handler;

            var webProxy = new WebProxy(proxy.Endpoint);
            if (!string.IsNullOrEmpty(proxy.Credentials))
            {
                var parts = proxy.Credentials!.Split(new[] { ':' }, 2);
                var userName = Uri.UnescapeDataString(parts[0]);
                var password = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                webProxy.Credentials = new NetworkCredential(userName, password);
            }
            handler.Proxy = webProxy;
            handler.UseProxy = true;
            return handler;
        }

        private static async Task<DiscordGuildCommandSynchronizationResult> SendCoreAsync(
            HttpClient client,
            HttpRequestMessage message,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status >= 200 && status <= 299)
                    return DiscordGuildCommandSynchronizationResult.Succeeded();
                if (status == 429)
                {
                    var retryAfter = ParseRetryAfterHeader(response, DateTimeOffset.UtcNow);
                    if (!retryAfter.HasValue)
                        retryAfter = await ParseRetryAfterBodyAsync(response).ConfigureAwait(false);
                    return DiscordGuildCommandSynchronizationResult.Retryable(
                        "discord_guild_command_sync_rate_limited",
                        retryAfter);
                }
                if (status >= 500 && status <= 599)
                    return DiscordGuildCommandSynchronizationResult.Retryable(
                        "discord_guild_command_sync_server_rejected",
                        null);
                return DiscordGuildCommandSynchronizationResult.Rejected(
                    "discord_guild_command_sync_rejected");
            }
            catch (OperationCanceledException)
            {
                return DiscordGuildCommandSynchronizationResult.ResultUnknown();
            }
            catch (HttpRequestException)
            {
                return DiscordGuildCommandSynchronizationResult.ResultUnknown();
            }
            catch
            {
                return DiscordGuildCommandSynchronizationResult.ResultUnknown();
            }
        }

        private static TimeSpan? ParseRetryAfterHeader(
            HttpResponseMessage response,
            DateTimeOffset observedAtUtc)
        {
            if (!response.Headers.TryGetValues("Retry-After", out var values)) return null;
            var value = values.FirstOrDefault();
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
                return TimeSpan.FromSeconds(seconds);
            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var retryAt) &&
                retryAt > observedAtUtc)
                return retryAt - observedAtUtc;
            return null;
        }

        private static async Task<TimeSpan?> ParseRetryAfterBodyAsync(HttpResponseMessage response)
        {
            try
            {
                await response.Content.LoadIntoBufferAsync(16 * 1024).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var raw = JObject.Parse(json)["retry_after"];
                if (raw == null)
                    return null;
                if (!double.TryParse(
                        raw.ToString(Formatting.None),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var seconds) ||
                    seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
                    return null;
                return TimeSpan.FromSeconds(seconds);
            }
            catch
            {
                return null;
            }
        }
    }
}
