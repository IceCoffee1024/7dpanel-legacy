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
using System.Web.Script.Serialization;
using LSTY.SevenDPanel.Application.Discord;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public sealed class DiscordApiClient : IDiscordApiClient, IDisposable
    {
        private static readonly Uri ApiBaseAddress = new Uri("https://discord.com/api/v10/");
        private readonly HttpClient? fixedClient;
        private bool disposed;

        public DiscordApiClient() { }

        public DiscordApiClient(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            fixedClient = new HttpClient(handler, false);
        }

        public async Task<DiscordApiResult> SendAsync(
            DiscordApiRequest request,
            CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiscordApiClient));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Content == null || request.Content.Length < 1 || request.Content.Length > 2000)
                return DiscordApiResult.Failed("discord_content_invalid");

            HttpRequestMessage message;
            try
            {
                message = CreateMessage(request);
            }
            catch
            {
                return DiscordApiResult.Failed("discord_request_invalid");
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

        private static HttpRequestMessage CreateMessage(DiscordApiRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Credential))
                throw new InvalidOperationException("discord_credential_missing");

            Uri endpoint;
            object payload;
            var message = new HttpRequestMessage(HttpMethod.Post, ApiBaseAddress);
            try
            {
                if (request.Mode == DiscordIntegrationMode.Webhook)
                {
                    endpoint = WebhookEndpoint(request.Credential);
                    payload = new
                    {
                        content = request.Content,
                        allowed_mentions = new { parse = Array.Empty<string>() }
                    };
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(request.ChannelId) ||
                        string.IsNullOrWhiteSpace(request.Nonce))
                        throw new InvalidOperationException("discord_bot_request_invalid");
                    endpoint = new Uri(
                        ApiBaseAddress,
                        "channels/" + Uri.EscapeDataString(request.ChannelId) + "/messages");
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bot", request.Credential);
                    payload = new
                    {
                        content = request.Content,
                        nonce = request.Nonce,
                        enforce_nonce = true,
                        allowed_mentions = new { parse = Array.Empty<string>() }
                    };
                }

                message.RequestUri = endpoint;
                var json = new JavaScriptSerializer().Serialize(payload);
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return message;
            }
            catch
            {
                message.Dispose();
                throw;
            }
        }

        private static Uri WebhookEndpoint(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps ||
                (!string.Equals(endpoint.Host, "discord.com", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(endpoint.Host, "discordapp.com", StringComparison.OrdinalIgnoreCase)) ||
                endpoint.AbsolutePath.IndexOf("/webhooks/", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("discord_webhook_invalid");

            var query = endpoint.Query.TrimStart('?')
                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("wait=", StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { "wait=true" });
            var builder = new UriBuilder(endpoint) { Query = string.Join("&", query) };
            return builder.Uri;
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

        private static async Task<DiscordApiResult> SendCoreAsync(
            HttpClient client,
            HttpRequestMessage message,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.NoContent)
                    return DiscordApiResult.Succeeded();
                if (status == 429)
                {
                    var retryAfter = ParseRetryAfterHeader(response, DateTimeOffset.UtcNow);
                    if (!retryAfter.HasValue)
                        retryAfter = await ParseRetryAfterBodyAsync(response).ConfigureAwait(false);
                    return DiscordApiResult.Retryable("discord_rate_limited", retryAfter);
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                    return DiscordApiResult.Failed("discord_authentication_failed");
                if (status >= 500 && status <= 599)
                    return DiscordApiResult.Retryable("discord_server_rejected", null);
                return DiscordApiResult.Failed("discord_request_rejected");
            }
            catch (OperationCanceledException)
            {
                return DiscordApiResult.ResultUnknown();
            }
            catch (HttpRequestException)
            {
                return DiscordApiResult.ResultUnknown();
            }
            catch
            {
                return DiscordApiResult.ResultUnknown();
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
                var parsed = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
                if (parsed == null || !parsed.TryGetValue("retry_after", out var raw) || raw == null)
                    return null;
                if (!double.TryParse(
                        Convert.ToString(raw, CultureInfo.InvariantCulture),
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
