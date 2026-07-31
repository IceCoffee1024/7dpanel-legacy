using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class DiscordInteractionRestClientTests
    {
        private const string ApplicationId = "123456789012345678";
        private const string GuildId = "987654321098765432";
        private const string InteractionToken = "interaction-token-secret";
        private const string BotToken = "bot-token-secret";

        [Fact]
        public async Task Ephemeral_follow_up_uses_the_fixed_interaction_webhook_route_without_authorization()
        {
            CapturedRequest? captured = null;
            using var handler = new RecordingHandler(async (request, cancellationToken) =>
            {
                captured = await CapturedRequest.CreateAsync(request, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using var client = new DiscordInteractionFollowupClient(handler);
            var request = new DiscordInteractionFollowupRequest(
                ApplicationId,
                InteractionToken,
                "The server is ready.",
                null);

            var result = await client.SendEphemeralAsync(request, CancellationToken.None);

            Assert.Equal(DiscordInteractionFollowupDisposition.Succeeded, result.Disposition);
            var sent = Assert.IsType<CapturedRequest>(captured);
            Assert.Equal(HttpMethod.Post, sent.Method);
            Assert.Equal(
                "https://discord.com/api/v10/webhooks/123456789012345678/interaction-token-secret",
                sent.Uri);
            Assert.Null(sent.AuthorizationScheme);
            Assert.Null(sent.AuthorizationParameter);

            var payload = DeserializeObject(sent.Body);
            Assert.Equal("The server is ready.", (string?)payload["content"]);
            Assert.Equal(64, (int?)payload["flags"]);
            var mentions = Assert.IsType<JObject>(payload["allowed_mentions"]);
            Assert.Empty(Assert.IsType<JArray>(mentions["parse"]));
            Assert.DoesNotContain(InteractionToken, request.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(InteractionToken, result.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Follow_up_maps_rate_limit_delay_and_timeout_without_exposing_or_replaying_the_token()
        {
            var calls = 0;
            using var rateLimitedHandler = new RecordingHandler((_, _) =>
            {
                calls++;
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"retry_after\":99,\"message\":\"body-secret\"}")
                };
                response.Headers.TryAddWithoutValidation("Retry-After", "1.75");
                return Task.FromResult(response);
            });
            using var rateLimitedClient = new DiscordInteractionFollowupClient(rateLimitedHandler);

            var rateLimited = await rateLimitedClient.SendEphemeralAsync(
                FollowupRequest(),
                TestContext.Current.CancellationToken);

            Assert.Equal(DiscordInteractionFollowupDisposition.Retryable, rateLimited.Disposition);
            Assert.Equal("discord_interaction_followup_rate_limited", rateLimited.ErrorCode);
            Assert.Equal(TimeSpan.FromSeconds(1.75), rateLimited.RetryAfter);
            Assert.Equal(1, calls);
            Assert.DoesNotContain("body-secret", rateLimited.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(InteractionToken, rateLimited.ToString(), StringComparison.Ordinal);

            using var timeoutHandler = new RecordingHandler((_, _) =>
            {
                calls++;
                return Task.FromException<HttpResponseMessage>(
                    new TaskCanceledException("timeout " + InteractionToken));
            });
            using var timeoutClient = new DiscordInteractionFollowupClient(timeoutHandler);

            var unknown = await timeoutClient.SendEphemeralAsync(
                FollowupRequest(),
                TestContext.Current.CancellationToken);

            Assert.Equal(DiscordInteractionFollowupDisposition.ResultUnknown, unknown.Disposition);
            Assert.Equal("discord_interaction_followup_result_unknown", unknown.ErrorCode);
            Assert.Equal(2, calls);
            Assert.DoesNotContain(InteractionToken, unknown.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Follow_up_rejects_non_snowflake_application_ids_without_contacting_a_custom_url()
        {
            var calls = 0;
            using var handler = new RecordingHandler((_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            });
            using var client = new DiscordInteractionFollowupClient(handler);

            var result = await client.SendEphemeralAsync(
                new DiscordInteractionFollowupRequest(
                    "https://untrusted.example/discord",
                    InteractionToken,
                    "The server is ready.",
                    null),
                CancellationToken.None);

            Assert.Equal(DiscordInteractionFollowupDisposition.Rejected, result.Disposition);
            Assert.Equal("discord_interaction_followup_invalid", result.ErrorCode);
            Assert.Equal(0, calls);
            Assert.DoesNotContain(InteractionToken, result.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Guild_command_sync_uses_bot_authorization_and_the_fixed_typed_command_whitelist()
        {
            CapturedRequest? captured = null;
            using var handler = new RecordingHandler(async (request, cancellationToken) =>
            {
                captured = await CapturedRequest.CreateAsync(request, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var synchronizer = new DiscordGuildCommandSynchronizer(handler);
            var request = new DiscordGuildCommandSynchronizationRequest(
                ApplicationId,
                GuildId,
                BotToken,
                null);

            var result = await synchronizer.SynchronizeAsync(request, CancellationToken.None);

            Assert.Equal(DiscordGuildCommandSynchronizationDisposition.Succeeded, result.Disposition);
            var sent = Assert.IsType<CapturedRequest>(captured);
            Assert.Equal(HttpMethod.Put, sent.Method);
            Assert.Equal(
                "https://discord.com/api/v10/applications/123456789012345678/guilds/987654321098765432/commands",
                sent.Uri);
            Assert.Equal("Bot", sent.AuthorizationScheme);
            Assert.Equal(BotToken, sent.AuthorizationParameter);

            var definitions = JArray.Parse(sent.Body)
                .OfType<JObject>()
                .ToArray();
            Assert.Equal(3, definitions.Length);
            Assert.Equal(new[] { "bind", "status", "players" }, definitions.Select(command => (string?)command["name"]));
            Assert.All(definitions, command => Assert.Equal(1, (int?)command["type"]));
            var bindOptions = Assert.IsType<JArray>(definitions[0]["options"]);
            var code = Assert.IsType<JObject>(Assert.Single(bindOptions));
            Assert.Equal("code", (string?)code["name"]);
            Assert.Equal(3, (int?)code["type"]);
            Assert.True((bool?)code["required"]);
            Assert.DoesNotContain(BotToken, request.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BotToken, result.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Guild_command_sync_returns_the_retry_after_header_for_rate_limits()
        {
            using var handler = new RecordingHandler((_, _) =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429);
                response.Headers.TryAddWithoutValidation("Retry-After", "2.5");
                return Task.FromResult(response);
            });
            using var synchronizer = new DiscordGuildCommandSynchronizer(handler);

            var result = await synchronizer.SynchronizeAsync(Request(), CancellationToken.None);

            Assert.Equal(DiscordGuildCommandSynchronizationDisposition.Retryable, result.Disposition);
            Assert.Equal("discord_guild_command_sync_rate_limited", result.ErrorCode);
            Assert.Equal(TimeSpan.FromSeconds(2.5), result.RetryAfter);
        }

        [Fact]
        public async Task Guild_command_sync_returns_retry_after_from_a_rate_limit_body_when_header_is_missing()
        {
            using var handler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"retry_after\":3.25}")
                }));
            using var synchronizer = new DiscordGuildCommandSynchronizer(handler);

            var result = await synchronizer.SynchronizeAsync(Request(), CancellationToken.None);

            Assert.Equal(DiscordGuildCommandSynchronizationDisposition.Retryable, result.Disposition);
            Assert.Equal("discord_guild_command_sync_rate_limited", result.ErrorCode);
            Assert.Equal(TimeSpan.FromSeconds(3.25), result.RetryAfter);
        }

        [Fact]
        public async Task Guild_command_sync_maps_rejection_and_transport_failures_to_safe_stable_results()
        {
            const string responseBody = "COMMAND-SYNC-RESPONSE-BODY-SENTINEL";
            using var rejectedHandler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(responseBody)
                }));
            using var rejectedSynchronizer = new DiscordGuildCommandSynchronizer(rejectedHandler);

            var rejected = await rejectedSynchronizer.SynchronizeAsync(Request(), CancellationToken.None);

            Assert.Equal(DiscordGuildCommandSynchronizationDisposition.Rejected, rejected.Disposition);
            Assert.Equal("discord_guild_command_sync_rejected", rejected.ErrorCode);
            Assert.DoesNotContain(responseBody, rejected.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BotToken, rejected.ToString(), StringComparison.Ordinal);

            using var failedHandler = new RecordingHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException(BotToken)));
            using var failedSynchronizer = new DiscordGuildCommandSynchronizer(failedHandler);

            var unknown = await failedSynchronizer.SynchronizeAsync(Request(), CancellationToken.None);

            Assert.Equal(DiscordGuildCommandSynchronizationDisposition.ResultUnknown, unknown.Disposition);
            Assert.Equal("discord_guild_command_sync_result_unknown", unknown.ErrorCode);
            Assert.DoesNotContain(BotToken, unknown.ToString(), StringComparison.Ordinal);
        }

        private static DiscordGuildCommandSynchronizationRequest Request() =>
            new DiscordGuildCommandSynchronizationRequest(ApplicationId, GuildId, BotToken, null);

        private static DiscordInteractionFollowupRequest FollowupRequest() =>
            new DiscordInteractionFollowupRequest(
                ApplicationId,
                InteractionToken,
                "The server is ready.",
                null);

        private static JObject DeserializeObject(string json) => JObject.Parse(json);

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;

            public RecordingHandler(
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
                this.send = send;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => send(request, cancellationToken);
        }

        private sealed class CapturedRequest
        {
            private CapturedRequest(
                HttpMethod method,
                string uri,
                string? authorizationScheme,
                string? authorizationParameter,
                string body)
            {
                Method = method;
                Uri = uri;
                AuthorizationScheme = authorizationScheme;
                AuthorizationParameter = authorizationParameter;
                Body = body;
            }

            public HttpMethod Method { get; }
            public string Uri { get; }
            public string? AuthorizationScheme { get; }
            public string? AuthorizationParameter { get; }
            public string Body { get; }

            public static async Task<CapturedRequest> CreateAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new CapturedRequest(
                    request.Method,
                    request.RequestUri!.AbsoluteUri,
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter,
                    body);
            }
        }
    }
}
