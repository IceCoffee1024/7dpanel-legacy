using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Discord;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Application")]
    public sealed class DiscordDeliveryTests
    {
        private const string BotToken = "BOT-TOKEN-SENTINEL";
        private const string WebhookUrl =
            "https://discord.com/api/webhooks/123/WEBHOOK-TOKEN-SENTINEL";
        private const string ProxyUri =
            "http://proxy-user:PROXY-PASSWORD-SENTINEL@proxy.example:8080";
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Configuration_supports_modes_named_targets_safe_proxy_summary_and_optimistic_versions()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            var secrets = new SetDiscordSecretUseCase(store, () => Now);
            secrets.Execute(DiscordSecretKeys.BotToken, BotToken);
            secrets.Execute(DiscordSecretKeys.WebhookUrl("alerts"), WebhookUrl);

            var save = new SaveDiscordConfigurationUseCase(store, () => Now);
            var summary = save.Execute(new DiscordConfigurationUpdate(
                expectedVersion: 0,
                isEnabled: true,
                mode: DiscordIntegrationMode.Bot,
                applicationId: "app-1",
                guildId: "guild-1",
                publicChannelId: "channel-default",
                bridgeGameToDiscord: true,
                bridgeDiscordToGame: false,
                proxyEnabled: true,
                proxyUri: ProxyUri,
                targets: new[]
                {
                    new DiscordTarget("alerts", "Webhook", null, true),
                    new DiscordTarget("public", "Bot", "channel-public", true)
                }));

            Assert.Equal(1, summary.Version);
            Assert.Equal(DiscordIntegrationMode.Bot, summary.Mode);
            Assert.Equal("http://proxy.example:8080", summary.Proxy.Endpoint);
            Assert.True(summary.Proxy.HasCredentials);
            Assert.Equal(new[] { "alerts", "public" }, summary.Targets.Select(target => target.TargetKey));
            Assert.All(summary.Secrets, secret => Assert.True(secret.IsSet));
            Assert.DoesNotContain(BotToken, summary.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("WEBHOOK-TOKEN-SENTINEL", summary.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("PROXY-PASSWORD-SENTINEL", summary.ToString(), StringComparison.Ordinal);
            Assert.Equal("http://proxy.example:8080/", store.GetSettings()!.ProxyUri);
            Assert.Equal(
                "proxy-user:PROXY-PASSWORD-SENTINEL",
                store.GetSecret(DiscordSecretKeys.ProxyCredentials)!.SecretValue);

            Assert.Throws<DiscordIntegrationVersionConflictException>(() => save.Execute(
                new DiscordConfigurationUpdate(
                    0, true, DiscordIntegrationMode.Webhook, null, null, null,
                    false, false, false, null, Array.Empty<DiscordTarget>())));
        }

        [Fact]
        public void Secret_and_configuration_input_string_representations_are_redacted()
        {
            var secret = new DiscordSecretValue("botToken", BotToken, "fingerprint", Now);
            var update = new DiscordConfigurationUpdate(
                0, true, DiscordIntegrationMode.Bot, null, null, "channel",
                false, false, true, ProxyUri, Array.Empty<DiscordTarget>());
            var request = DiscordApiRequest.Bot("channel", BotToken, "message", "business-key", null);
            var proxy = new DiscordProxyConfiguration(new Uri(ProxyUri), "proxy-user:PROXY-PASSWORD-SENTINEL");

            Assert.DoesNotContain(BotToken, secret.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("PROXY-PASSWORD-SENTINEL", update.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BotToken, request.ToString(), StringComparison.Ordinal);
            Assert.Equal("http://proxy.example:8080/", proxy.Endpoint.AbsoluteUri);
            Assert.DoesNotContain("PROXY-PASSWORD-SENTINEL", proxy.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void Unset_secrets_are_reported_as_safe_metadata()
        {
            using var database = new TemporaryDatabase();

            var summary = new GetDiscordConfigurationUseCase(database.Store).Execute();

            Assert.False(summary.Secrets.Single(secret =>
                secret.SecretKey == DiscordSecretKeys.BotToken).IsSet);
            Assert.False(summary.Secrets.Single(secret =>
                secret.SecretKey == DiscordSecretKeys.ProxyCredentials).IsSet);
            Assert.DoesNotContain(
                typeof(DiscordSecretSummary).GetProperties(),
                property => property.Name == nameof(DiscordSecretValue.SecretValue));
        }

        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.NoContent)]
        public async Task Webhook_uses_wait_true_suppresses_mentions_and_accepts_200_or_204(
            HttpStatusCode statusCode)
        {
            HttpMethod? method = null;
            Uri? endpoint = null;
            string? body = null;
            using var handler = new RecordingHandler(async (request, _) =>
            {
                method = request.Method;
                endpoint = request.RequestUri;
                body = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(statusCode == HttpStatusCode.OK ? "{\"id\":\"42\"}" : string.Empty)
                };
            });
            using var client = new DiscordApiClient(handler);

            var result = await client.SendAsync(
                DiscordApiRequest.Webhook(WebhookUrl, "hello @everyone", null),
                CancellationToken.None);

            Assert.Equal(DiscordApiDeliveryDisposition.Succeeded, result.Disposition);
            Assert.Equal(HttpMethod.Post, method);
            Assert.NotNull(endpoint);
            Assert.Equal("true", ParseQuery(endpoint!).Single(pair => pair.Key == "wait").Value);
            Assert.Contains("\"content\":\"hello @everyone\"", body, StringComparison.Ordinal);
            Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Bot_uses_v10_channel_route_exact_authorization_and_nonce()
        {
            Uri? endpoint = null;
            string? authorization = null;
            string? body = null;
            using var handler = new RecordingHandler(async (request, _) =>
            {
                endpoint = request.RequestUri;
                authorization = request.Headers.Authorization?.ToString();
                body = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"43\"}")
                };
            });
            using var client = new DiscordApiClient(handler);

            var result = await client.SendAsync(
                DiscordApiRequest.Bot("channel-42", BotToken, "hello", "business-42", null),
                CancellationToken.None);

            Assert.Equal(DiscordApiDeliveryDisposition.Succeeded, result.Disposition);
            Assert.Equal("https://discord.com/api/v10/channels/channel-42/messages", endpoint!.AbsoluteUri);
            Assert.Equal("Bot " + BotToken, authorization);
            Assert.Contains("\"nonce\":\"business-42\"", body, StringComparison.Ordinal);
            Assert.Contains("\"enforce_nonce\":true", body, StringComparison.Ordinal);
            Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Rate_limit_prefers_retry_after_header_over_json_body()
        {
            using var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"retry_after\":99.5,\"message\":\"BODY-SENTINEL\"}")
            };
            response.Headers.TryAddWithoutValidation("Retry-After", "7.25");
            using var handler = new RecordingHandler((_, _) => Task.FromResult(response));
            using var client = new DiscordApiClient(handler);

            var result = await client.SendAsync(
                DiscordApiRequest.Bot("channel", BotToken, "hello", "key", null),
                CancellationToken.None);

            Assert.Equal(DiscordApiDeliveryDisposition.Retryable, result.Disposition);
            Assert.Equal(TimeSpan.FromSeconds(7.25), result.RetryAfter);
            Assert.Equal("discord_rate_limited", result.ErrorCode);
            Assert.DoesNotContain("BODY-SENTINEL", result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BotToken, result.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Rate_limit_uses_json_retry_after_when_header_is_missing()
        {
            using var handler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"retry_after\":2.5}")
                }));
            using var client = new DiscordApiClient(handler);

            var result = await client.SendAsync(
                DiscordApiRequest.Bot("channel", BotToken, "hello", "key", null),
                CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(2.5), result.RetryAfter);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task Authentication_rejection_is_terminal_and_response_body_is_not_exposed(
            HttpStatusCode statusCode)
        {
            using var handler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent("AUTH-RESPONSE-BODY-SENTINEL")
                }));
            using var client = new DiscordApiClient(handler);

            var result = await client.SendAsync(
                DiscordApiRequest.Bot("channel", BotToken, "hello", "key", null),
                CancellationToken.None);

            Assert.Equal(DiscordApiDeliveryDisposition.Failed, result.Disposition);
            Assert.Equal("discord_authentication_failed", result.ErrorCode);
            Assert.DoesNotContain("AUTH-RESPONSE-BODY-SENTINEL", result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BotToken, result.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Connection_failure_and_timeout_are_result_unknown_without_exception_details()
        {
            var failures = new Exception[]
            {
                new HttpRequestException("connection failed " + WebhookUrl),
                new TaskCanceledException("timeout " + BotToken)
            };

            foreach (var failure in failures)
            {
                using var handler = new RecordingHandler((_, _) => Task.FromException<HttpResponseMessage>(failure));
                using var client = new DiscordApiClient(handler);
                var result = await client.SendAsync(
                    DiscordApiRequest.Bot("channel", BotToken, "hello", "key", null),
                    CancellationToken.None);

                Assert.Equal(DiscordApiDeliveryDisposition.ResultUnknown, result.Disposition);
                Assert.Equal("discord_delivery_result_unknown", result.ErrorCode);
                Assert.DoesNotContain(BotToken, result.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain("WEBHOOK-TOKEN-SENTINEL", result.ToString(), StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task Worker_retries_explicit_rejection_with_exponential_backoff_then_clears_content()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var now = Now;
            var delivery = Enqueue(database.Store, "business-retry", "public", "hello retry");
            var api = new RecordingDiscordApiClient(
                DiscordApiResult.Retryable("discord_server_rejected", null),
                DiscordApiResult.Succeeded());
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => now, TimeSpan.FromMilliseconds(1));

            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            var scheduled = database.Store.FindDelivery(delivery.DeliveryId)!;
            Assert.Equal(DiscordDeliveryStatus.RetryScheduled, scheduled.Status);
            Assert.Equal(Now.AddSeconds(2), scheduled.NextAttemptAtUtc);
            Assert.Equal(1, scheduled.RetryCount);
            Assert.False(await worker.ProcessNextAsync(CancellationToken.None));

            now = Now.AddSeconds(2);
            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            var succeeded = database.Store.FindDelivery(delivery.DeliveryId)!;
            Assert.Equal(DiscordDeliveryStatus.Succeeded, succeeded.Status);
            Assert.Null(succeeded.ContentText);
            Assert.Equal(new[] { 1, 2 }, database.Store.ListDeliveryAttempts(delivery.DeliveryId)
                .Select(attempt => attempt.AttemptNumber));
        }

        [Fact]
        public async Task Worker_honors_official_rate_limit_delay_and_caps_only_exponential_backoff()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var delivery = Enqueue(database.Store, "business-rate-limit", "public", "hello rate limit");
            var api = new RecordingDiscordApiClient(
                DiscordApiResult.Retryable("discord_rate_limited", TimeSpan.FromMinutes(7)));
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => Now, TimeSpan.FromMilliseconds(1));

            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            Assert.Equal(
                Now.AddMinutes(7),
                database.Store.FindDelivery(delivery.DeliveryId)!.NextAttemptAtUtc);
            Assert.Equal(TimeSpan.FromMinutes(5), DiscordDeliveryPolicy.ExponentialDelay(20));
        }

        [Fact]
        public async Task Unknown_result_is_not_automatically_retried_and_restart_recovers_sending()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var unknown = Enqueue(database.Store, "business-unknown", "public", "hello unknown");
            var api = new RecordingDiscordApiClient(DiscordApiResult.ResultUnknown());
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => Now, TimeSpan.FromMilliseconds(1));

            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            Assert.Equal(DiscordDeliveryStatus.ResultUnknown, database.Store.FindDelivery(unknown.DeliveryId)!.Status);
            Assert.Null(database.Store.FindDelivery(unknown.DeliveryId)!.ContentText);
            Assert.False(await worker.ProcessNextAsync(CancellationToken.None));

            var interrupted = Enqueue(database.Store, "business-restart", "public", "hello restart");
            var claimed = database.Store.TryClaimNextDeliveryAttempt(Now.AddSeconds(1));
            Assert.Equal(interrupted.DeliveryId, claimed!.Delivery.DeliveryId);
            Assert.Equal(1, worker.RecoverInterrupted());
            var recovered = database.Store.FindDelivery(interrupted.DeliveryId)!;
            Assert.Equal(DiscordDeliveryStatus.ResultUnknown, recovered.Status);
            Assert.Null(recovered.ContentText);
            Assert.Equal("discord_restart_result_unknown", Assert.Single(
                database.Store.ListDeliveryAttempts(interrupted.DeliveryId)).ErrorCode);
        }

        [Fact]
        public async Task Worker_performs_at_most_five_automatic_retries()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var now = Now;
            var delivery = Enqueue(database.Store, "business-max-retry", "public", "hello max retry");
            var api = new RecordingDiscordApiClient(Enumerable.Range(0, 6)
                .Select(_ => DiscordApiResult.Retryable("discord_server_rejected", null))
                .ToArray());
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => now, TimeSpan.FromMilliseconds(1));

            for (var attempt = 1; attempt <= 6; attempt++)
            {
                Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
                var current = database.Store.FindDelivery(delivery.DeliveryId)!;
                if (current.NextAttemptAtUtc.HasValue) now = current.NextAttemptAtUtc.Value;
            }

            var failed = database.Store.FindDelivery(delivery.DeliveryId)!;
            Assert.Equal(DiscordDeliveryStatus.Failed, failed.Status);
            Assert.Equal(5, failed.RetryCount);
            Assert.Null(failed.ContentText);
            Assert.Equal(6, database.Store.ListDeliveryAttempts(delivery.DeliveryId).Count);
        }

        [Fact]
        public async Task Manual_retry_increments_attempt_and_preserves_business_key()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var now = Now;
            var delivery = Enqueue(database.Store, "business-manual", "public", "hello manual");
            var api = new RecordingDiscordApiClient(
                DiscordApiResult.Failed("discord_authentication_failed"),
                DiscordApiResult.Succeeded());
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => now, TimeSpan.FromMilliseconds(1));

            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            Assert.Null(database.Store.FindDelivery(delivery.DeliveryId)!.ContentText);
            var retried = new RetryDiscordDeliveryUseCase(database.Store, () => Now.AddMinutes(1))
                .Execute(delivery.DeliveryId, "hello manual");

            Assert.Equal("business-manual", retried.BusinessKey);
            Assert.Equal(DiscordDeliveryStatus.RetryScheduled, retried.Status);
            now = Now.AddMinutes(1);
            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            Assert.Equal(new[] { 1, 2 }, database.Store.ListDeliveryAttempts(delivery.DeliveryId)
                .Select(attempt => attempt.AttemptNumber));
            Assert.Equal("business-manual", database.Store.FindDelivery(delivery.DeliveryId)!.BusinessKey);
        }

        [Fact]
        public void Cancelled_is_a_persisted_terminal_state_and_clears_content()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var delivery = Enqueue(database.Store, "business-cancel", "public", "hello cancel");
            var claimed = database.Store.TryClaimNextDeliveryAttempt(Now)!;

            database.Store.CompleteDeliveryAttempt(
                delivery.DeliveryId,
                claimed.AttemptNumber,
                DiscordDeliveryStatus.Cancelled,
                Now.AddSeconds(1),
                "discord_delivery_cancelled",
                null);

            var cancelled = database.Store.FindDelivery(delivery.DeliveryId)!;
            Assert.Equal(DiscordDeliveryStatus.Cancelled, cancelled.Status);
            Assert.Null(cancelled.ContentText);
            Assert.Equal(DiscordDeliveryStatus.Cancelled, Assert.Single(
                database.Store.ListDeliveryAttempts(delivery.DeliveryId)).Status);
        }

        [Fact]
        public async Task Disabling_blocks_new_delivery_preserves_configuration_and_drains_accepted_items()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var accepted = Enqueue(database.Store, "business-accepted", "public", "hello accepted");
            var current = database.Store.GetSettings()!;
            database.Store.SaveSettings(
                current with { Version = 2, IsEnabled = false, UpdatedAtUtc = Now.AddSeconds(1) },
                expectedVersion: 1);

            Assert.Throws<DiscordIntegrationDisabledException>(() =>
                Enqueue(database.Store, "business-blocked", "public", "hello blocked"));
            Assert.Equal(BotToken, database.Store.GetSecret(DiscordSecretKeys.BotToken)!.SecretValue);
            Assert.True(Assert.Single(database.Store.ListTargets()).IsEnabled);

            var api = new RecordingDiscordApiClient(DiscordApiResult.Succeeded());
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => Now.AddSeconds(2), TimeSpan.FromMilliseconds(1));
            var drained = await worker.DrainAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal(1, drained);
            Assert.Equal(DiscordDeliveryStatus.Succeeded, database.Store.FindDelivery(accepted.DeliveryId)!.Status);
        }

        [Fact]
        public async Task Drain_timeout_is_bounded_when_the_api_ignores_cancellation()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var delivery = Enqueue(database.Store, "business-bounded-drain", "public", "hello drain");
            var release = new TaskCompletionSource<DiscordApiResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sendStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var api = new DelegateDiscordApiClient((_, _) =>
            {
                sendStarted.TrySetResult(true);
                return release.Task;
            });
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => Now, TimeSpan.FromMilliseconds(1));
            var stopwatch = Stopwatch.StartNew();
            var drain = Task.Factory.StartNew(
                    () => worker.DrainAsync(
                        TimeSpan.FromMilliseconds(50),
                        CancellationToken.None),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();

            try
            {
                var started = await Task.WhenAny(
                    sendStarted.Task,
                    Task.Delay(TimeSpan.FromSeconds(1)));
                Assert.Same(sendStarted.Task, started);
                var completed = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(1)));
                Assert.Same(drain, completed);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
                Assert.Equal(1, await drain);
                Assert.Equal(
                    DiscordDeliveryStatus.ResultUnknown,
                    database.Store.FindDelivery(delivery.DeliveryId)!.Status);
            }
            finally
            {
                release.TrySetResult(DiscordApiResult.Succeeded());
                await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(1)));
            }
        }

        [Fact]
        public async Task Worker_maps_named_webhook_and_bot_targets_with_proxy_without_logging_secrets()
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Webhook, proxyUri: ProxyUri);
            database.Store.SetSecret(new DiscordSecretValue(
                DiscordSecretKeys.WebhookUrl("alerts"), WebhookUrl, "webhook-fp", Now));
            database.Store.SaveTarget(new DiscordTarget("alerts", "Webhook", null, true));
            var delivery = Enqueue(database.Store, "business-target", "alerts", "hello target");
            var logs = new List<string>();
            var api = new RecordingDiscordApiClient(DiscordApiResult.Succeeded());
            using var worker = new DiscordDeliveryWorker(
                database.Store, api, () => Now, TimeSpan.FromMilliseconds(1), logs.Add);

            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));

            var request = Assert.Single(api.Requests);
            Assert.Equal(DiscordIntegrationMode.Webhook, request.Mode);
            Assert.Equal(WebhookUrl, request.Credential);
            Assert.Equal("http://proxy.example:8080/", request.Proxy!.Endpoint.AbsoluteUri);
            Assert.Equal("proxy-user:PROXY-PASSWORD-SENTINEL", request.Proxy.Credentials);
            Assert.DoesNotContain(WebhookUrl, request.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BotToken, string.Join("\n", logs), StringComparison.Ordinal);
            Assert.DoesNotContain("WEBHOOK-TOKEN-SENTINEL", string.Join("\n", logs), StringComparison.Ordinal);
            Assert.DoesNotContain("PROXY-PASSWORD-SENTINEL", string.Join("\n", logs), StringComparison.Ordinal);
            Assert.Equal(DiscordDeliveryStatus.Succeeded, database.Store.FindDelivery(delivery.DeliveryId)!.Status);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2001)]
        public void Content_outside_discord_limits_is_rejected_without_persistence(int length)
        {
            using var database = ConfiguredDatabase(DiscordIntegrationMode.Bot);
            var content = new string('x', length);

            Assert.Throws<DiscordDeliveryValidationException>(() =>
                Enqueue(database.Store, "business-invalid", "public", content));
            Assert.Null(database.Store.FindDelivery("delivery-business-invalid"));
        }

        private static DiscordDeliverySummary Enqueue(
            SqliteDiscordIntegrationStore store,
            string businessKey,
            string targetKey,
            string content)
        {
            return new EnqueueDiscordDeliveryUseCase(
                    store,
                    () => Now,
                    () => "delivery-" + businessKey)
                .Execute(businessKey, targetKey, content);
        }

        private static TemporaryDatabase ConfiguredDatabase(
            DiscordIntegrationMode mode,
            string? proxyUri = null)
        {
            var database = new TemporaryDatabase();
            var proxy = proxyUri == null ? null : new Uri(proxyUri);
            var safeProxy = proxy == null
                ? null
                : new UriBuilder(proxy) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
            database.Store.SaveSettings(new DiscordIntegrationSettings(
                1,
                true,
                mode,
                "app",
                "guild",
                "channel-default",
                false,
                false,
                proxy != null,
                safeProxy,
                Now), expectedVersion: 0);
            database.Store.SaveTarget(new DiscordTarget("public", "Bot", "channel-public", true));
            database.Store.SetSecret(new DiscordSecretValue(
                DiscordSecretKeys.BotToken, BotToken, "bot-fp", Now));
            if (proxy != null)
            {
                database.Store.SetSecret(new DiscordSecretValue(
                    DiscordSecretKeys.ProxyCredentials,
                    proxy.UserInfo,
                    "proxy-fp",
                    Now));
            }
            return database;
        }

        private static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(Uri uri)
        {
            return uri.Query.TrimStart('?')
                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split(new[] { '=' }, 2))
                .Select(parts => new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(parts[0]),
                    parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty))
                .ToArray();
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

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

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingDiscordApiClient : IDiscordApiClient
        {
            private readonly Queue<DiscordApiResult> results;

            public RecordingDiscordApiClient(params DiscordApiResult[] results) =>
                this.results = new Queue<DiscordApiResult>(results);

            public List<DiscordApiRequest> Requests { get; } = new List<DiscordApiRequest>();

            public Task<DiscordApiResult> SendAsync(
                DiscordApiRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<DiscordApiResult>(cancellationToken);
                return Task.FromResult(results.Dequeue());
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class DelegateDiscordApiClient : IDiscordApiClient
        {
            private readonly Func<DiscordApiRequest, CancellationToken, Task<DiscordApiResult>> send;

            public DelegateDiscordApiClient(
                Func<DiscordApiRequest, CancellationToken, Task<DiscordApiResult>> send) =>
                this.send = send;

            public Task<DiscordApiResult> SendAsync(
                DiscordApiRequest request,
                CancellationToken cancellationToken) => send(request, cancellationToken);
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-discord-delivery-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
                Store = new SqliteDiscordIntegrationStore(ConnectionFactory);
            }

            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteDiscordIntegrationStore Store { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
