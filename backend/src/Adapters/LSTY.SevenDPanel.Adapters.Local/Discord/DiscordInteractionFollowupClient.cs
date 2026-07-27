using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Discord;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public enum DiscordInteractionFollowupDisposition
    {
        Succeeded,
        Retryable,
        Rejected,
        ResultUnknown
    }

    public sealed class DiscordInteractionFollowupResult
    {
        private DiscordInteractionFollowupResult(
            DiscordInteractionFollowupDisposition disposition,
            string? errorCode,
            TimeSpan? retryAfter)
        {
            Disposition = disposition;
            ErrorCode = errorCode;
            RetryAfter = retryAfter;
        }

        public DiscordInteractionFollowupDisposition Disposition { get; }
        public string? ErrorCode { get; }
        public TimeSpan? RetryAfter { get; }

        public static DiscordInteractionFollowupResult Succeeded() =>
            new DiscordInteractionFollowupResult(
                DiscordInteractionFollowupDisposition.Succeeded,
                null,
                null);

        public static DiscordInteractionFollowupResult Retryable(
            string errorCode,
            TimeSpan? retryAfter) =>
            new DiscordInteractionFollowupResult(
                DiscordInteractionFollowupDisposition.Retryable,
                RequireErrorCode(errorCode),
                retryAfter);

        public static DiscordInteractionFollowupResult Rejected(string errorCode) =>
            new DiscordInteractionFollowupResult(
                DiscordInteractionFollowupDisposition.Rejected,
                RequireErrorCode(errorCode),
                null);

        public static DiscordInteractionFollowupResult ResultUnknown() =>
            new DiscordInteractionFollowupResult(
                DiscordInteractionFollowupDisposition.ResultUnknown,
                "discord_interaction_followup_result_unknown",
                null);

        public override string ToString() =>
            $"DiscordInteractionFollowupResult {{ Disposition = {Disposition}, ErrorCode = {ErrorCode}, RetryAfter = {RetryAfter} }}";

        private static string RequireErrorCode(string errorCode) =>
            string.IsNullOrWhiteSpace(errorCode)
                ? throw new ArgumentException("An error code is required.", nameof(errorCode))
                : errorCode;
    }

    public sealed class DiscordInteractionFollowupRequest
    {
        public DiscordInteractionFollowupRequest(
            string applicationId,
            string interactionToken,
            string content,
            DiscordProxyConfiguration? proxy)
        {
            ApplicationId = applicationId;
            InteractionToken = interactionToken;
            Content = content;
            Proxy = proxy;
        }

        public string ApplicationId { get; }
        public string InteractionToken { get; }
        public string Content { get; }
        public DiscordProxyConfiguration? Proxy { get; }

        public override string ToString() =>
            $"DiscordInteractionFollowupRequest {{ ApplicationId = {ApplicationId}, InteractionToken = [REDACTED], ContentLength = {Content?.Length ?? 0}, Proxy = {Proxy} }}";
    }

    public sealed class DiscordInteractionFollowupClient : IDisposable, IDiscordInteractionResponseSender
    {
        private static readonly Uri ApiBaseAddress = new Uri("https://discord.com/api/v10/");
        private readonly HttpClient? fixedClient;
        private bool disposed;

        public DiscordInteractionFollowupClient() { }

        public DiscordInteractionFollowupClient(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            fixedClient = new HttpClient(handler, false);
        }

        public async Task<DiscordInteractionFollowupResult> SendEphemeralAsync(
            DiscordInteractionFollowupRequest request,
            CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiscordInteractionFollowupClient));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!IsValid(request))
                return DiscordInteractionFollowupResult.Rejected("discord_interaction_followup_invalid");

            HttpRequestMessage message;
            try
            {
                message = CreateMessage(request);
            }
            catch
            {
                return DiscordInteractionFollowupResult.Rejected("discord_interaction_followup_invalid");
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

        public async Task<DiscordInteractionResponseDisposition> SendEphemeralAsync(
            DiscordInteractionResponse response,
            CancellationToken cancellationToken)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            var result = await SendEphemeralAsync(
                    new DiscordInteractionFollowupRequest(
                        response.ApplicationId,
                        response.InteractionToken,
                        response.Content,
                        response.Proxy),
                    cancellationToken)
                .ConfigureAwait(false);
            switch (result.Disposition)
            {
                case DiscordInteractionFollowupDisposition.Succeeded:
                    return DiscordInteractionResponseDisposition.Succeeded;
                case DiscordInteractionFollowupDisposition.Retryable:
                    return DiscordInteractionResponseDisposition.Retryable;
                case DiscordInteractionFollowupDisposition.Rejected:
                    return DiscordInteractionResponseDisposition.Rejected;
                default:
                    return DiscordInteractionResponseDisposition.ResultUnknown;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            fixedClient?.Dispose();
        }

        private static bool IsValid(DiscordInteractionFollowupRequest request) =>
            IsSnowflake(request.ApplicationId) &&
            !string.IsNullOrWhiteSpace(request.InteractionToken) &&
            request.Content != null &&
            request.Content.Length >= 1 &&
            request.Content.Length <= 2000;

        private static bool IsSnowflake(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value!.Length <= 20 &&
            value.All(character => character >= '0' && character <= '9');

        private static HttpRequestMessage CreateMessage(DiscordInteractionFollowupRequest request)
        {
            var endpoint = new Uri(
                ApiBaseAddress,
                "webhooks/" + request.ApplicationId + "/" +
                Uri.EscapeDataString(request.InteractionToken));
            var payload = new
            {
                content = request.Content,
                flags = 64,
                allowed_mentions = new { parse = Array.Empty<string>() }
            };
            var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            return message;
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

        private static async Task<DiscordInteractionFollowupResult> SendCoreAsync(
            HttpClient client,
            HttpRequestMessage message,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status >= 200 && status <= 299)
                    return DiscordInteractionFollowupResult.Succeeded();
                if (status == 429)
                {
                    var retryAfter = ParseRetryAfterHeader(response, DateTimeOffset.UtcNow);
                    if (!retryAfter.HasValue)
                        retryAfter = await ParseRetryAfterBodyAsync(response).ConfigureAwait(false);
                    return DiscordInteractionFollowupResult.Retryable(
                        "discord_interaction_followup_rate_limited",
                        retryAfter);
                }
                if (status >= 500 && status <= 599)
                    return DiscordInteractionFollowupResult.Retryable(
                        "discord_interaction_followup_server_rejected",
                        null);
                return DiscordInteractionFollowupResult.Rejected(
                    "discord_interaction_followup_rejected");
            }
            catch (OperationCanceledException)
            {
                return DiscordInteractionFollowupResult.ResultUnknown();
            }
            catch (HttpRequestException)
            {
                return DiscordInteractionFollowupResult.ResultUnknown();
            }
            catch
            {
                return DiscordInteractionFollowupResult.ResultUnknown();
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
