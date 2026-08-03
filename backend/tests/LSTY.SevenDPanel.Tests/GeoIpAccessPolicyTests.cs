using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.GeoIp;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.AccessPolicies;
using LSTY.SevenDPanel.Application.GeoIp;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Application")]
    public sealed class GeoIpAccessPolicyTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData("203.0.113.7", "203.0.113.7", false)]
        [InlineData("::ffff:203.0.113.7", "203.0.113.7", false)]
        [InlineData("2001:0DB8:0:0:0:0:0:7", "2001:db8::7", false)]
        [InlineData("10.0.0.1", "10.0.0.1", true)]
        [InlineData("fc00::1", "fc00::1", true)]
        public void Address_normalization_handles_ipv4_ipv6_mapped_and_private_ranges(
            string input,
            string canonical,
            bool isPrivate)
        {
            Assert.True(GeoIpAddressNormalizer.TryNormalize(input, out var result));
            Assert.Equal(canonical, result!.CanonicalIp);
            Assert.Equal(isPrivate, result.IsPrivate);
        }

        [Fact]
        public void Cidr_matching_honors_network_boundaries_for_both_families()
        {
            var ipv4 = GeoIpNetwork.Parse("203.0.113.0/25");
            var ipv6 = GeoIpNetwork.Parse("2001:db8:abcd::/48");

            Assert.True(ipv4.Contains(IPAddress.Parse("203.0.113.127")));
            Assert.False(ipv4.Contains(IPAddress.Parse("203.0.113.128")));
            Assert.True(ipv6.Contains(IPAddress.Parse("2001:db8:abcd:ffff::1")));
            Assert.False(ipv6.Contains(IPAddress.Parse("2001:db8:abce::1")));
            Assert.Throws<FormatException>(() => GeoIpNetwork.Parse("203.0.113.0/33"));
        }

        [Fact]
        public void Policy_priority_is_deny_admin_bypass_allow_country_then_failure_mode()
        {
            var evaluator = new GeoIpPolicyEvaluator();
            var settings = Settings(GeoIpFailureMode.FailClosed, bypassAdmins: true);
            var countryRules = new[] { new GeoIpCountryRule("US", "Deny") };

            var deniedAdmin = evaluator.Evaluate(
                settings,
                new[]
                {
                    new GeoIpNetworkRule("allow", "203.0.113.9", "Allow", 0),
                    new GeoIpNetworkRule("deny", "203.0.113.0/24", "Deny", 1)
                },
                countryRules,
                "203.0.113.9",
                isConfirmedNativeAdministrator: true,
                GeoIpLookupResult.Found("US", "fixture", "v1"));
            Assert.False(deniedAdmin.IsAllowed);
            Assert.Equal("network_deny", deniedAdmin.ReasonCode);

            var bypassedAdmin = evaluator.Evaluate(
                settings,
                Array.Empty<GeoIpNetworkRule>(),
                countryRules,
                "203.0.113.9",
                isConfirmedNativeAdministrator: true,
                GeoIpLookupResult.Found("US", "fixture", "v1"));
            Assert.True(bypassedAdmin.IsAllowed);
            Assert.Equal("native_admin_bypass", bypassedAdmin.ReasonCode);

            var networkAllowed = evaluator.Evaluate(
                settings,
                new[] { new GeoIpNetworkRule("allow", "203.0.113.9", "Allow", 0) },
                countryRules,
                "203.0.113.9",
                isConfirmedNativeAdministrator: false,
                GeoIpLookupResult.Found("US", "fixture", "v1"));
            Assert.True(networkAllowed.IsAllowed);
            Assert.Equal("network_allow", networkAllowed.ReasonCode);

            var countryDenied = evaluator.Evaluate(
                settings,
                Array.Empty<GeoIpNetworkRule>(),
                countryRules,
                "203.0.113.9",
                isConfirmedNativeAdministrator: false,
                GeoIpLookupResult.Found("US", "fixture", "v1"));
            Assert.False(countryDenied.IsAllowed);
            Assert.Equal("country_deny", countryDenied.ReasonCode);
        }

        [Fact]
        public void Unknown_private_invalid_and_unavailable_follow_failure_mode_without_leaking_details()
        {
            var evaluator = new GeoIpPolicyEvaluator();
            var failOpen = Settings(GeoIpFailureMode.FailOpen, rejectionMessage: "请联系服主");
            var failClosed = Settings(GeoIpFailureMode.FailClosed, rejectionMessage: "请联系服主");

            foreach (var lookup in new[]
                     {
                         GeoIpLookupResult.Unknown("LocalMmdb", "db-secret-version"),
                         GeoIpLookupResult.Unavailable(
                             "MaxMindWebService",
                             GeoIpLookupFailure.Http,
                             "provider-secret-version")
                     })
            {
                var allowed = evaluator.Evaluate(
                    failOpen,
                    Array.Empty<GeoIpNetworkRule>(),
                    Array.Empty<GeoIpCountryRule>(),
                    "203.0.113.20",
                    false,
                    lookup);
                Assert.True(allowed.IsAllowed);
                Assert.Null(allowed.RejectionMessage);

                var denied = evaluator.Evaluate(
                    failClosed,
                    Array.Empty<GeoIpNetworkRule>(),
                    Array.Empty<GeoIpCountryRule>(),
                    "203.0.113.20",
                    false,
                    lookup);
                Assert.False(denied.IsAllowed);
                Assert.Equal("请联系服主", denied.RejectionMessage);
                Assert.DoesNotContain("MaxMind", denied.RejectionMessage, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Http", denied.RejectionMessage, StringComparison.OrdinalIgnoreCase);
            }

            var privateDecision = evaluator.Evaluate(
                failClosed,
                Array.Empty<GeoIpNetworkRule>(),
                Array.Empty<GeoIpCountryRule>(),
                "192.168.1.3",
                false,
                GeoIpLookupResult.Found("US", "fixture", "v1"));
            Assert.Equal(GeoIpLookupStatus.Private, privateDecision.LookupStatus);

            var invalidDecision = evaluator.Evaluate(
                failClosed,
                Array.Empty<GeoIpNetworkRule>(),
                Array.Empty<GeoIpCountryRule>(),
                "not-an-ip",
                false,
                GeoIpLookupResult.Found("US", "fixture", "v1"));
            Assert.Equal(GeoIpLookupStatus.Invalid, invalidDecision.LookupStatus);
        }

        [Fact]
        public void Default_fail_open_is_a_high_visibility_diagnostic()
        {
            var store = new MemoryGeoIpStore { Settings = Settings(GeoIpFailureMode.FailOpen) };
            var useCase = new GetGeoIpDiagnosticsUseCase(
                store,
                new StaticRefreshDiagnostics(
                    new GeoIpRefreshDiagnostics(
                        true,
                        0,
                        0,
                        null,
                        null,
                        new[]
                        {
                            new GeoIpProviderMetadata("LocalMmdb", false, "digest-1", "1785024000")
                        })));

            var result = useCase.Execute();

            Assert.Equal(GeoIpDiagnosticSeverity.Warning, result.Severity);
            Assert.Equal("fail_open_active", result.StatusCode);
            Assert.Equal(GeoIpFailureMode.FailOpen, result.FailureMode);
            Assert.Equal("digest-1", Assert.Single(result.Providers).SourceVersion);
        }

        [Fact]
        public void Fresh_failure_cache_is_used_until_ttl_and_decisions_store_only_masked_ip()
        {
            var store = new MemoryGeoIpStore
            {
                Settings = Settings(GeoIpFailureMode.FailClosed),
                Cache = new GeoIpCacheEntry(
                    "203.0.113.42",
                    GeoIpLookupStatus.Unavailable.ToString(),
                    null,
                    "MaxMindWebService",
                    "service-digest",
                    Now.AddMinutes(-1),
                    Now.AddMinutes(4))
            };
            var queue = new RecordingRefreshQueue();
            var useCase = new EvaluateGeoIpJoinUseCase(
                store,
                new GeoIpPolicyEvaluator(),
                queue,
                () => Now);

            var decision = useCase.Execute(new GeoIpJoinAttempt(
                "::ffff:203.0.113.42",
                "EOS_123",
                false));

            Assert.False(decision.IsAllowed);
            Assert.True(decision.WasCacheHit);
            Assert.False(decision.RefreshEnqueued);
            Assert.Empty(queue.Requests);
            Assert.Equal("203.0.113.0/24", Assert.Single(store.Decisions).MaskedIp);
            Assert.DoesNotContain("203.0.113.42", store.Decisions[0].MaskedIp, StringComparison.Ordinal);
        }

        [Fact]
        public void Expired_cache_miss_applies_failure_mode_and_only_enqueues_background_refresh()
        {
            var store = new MemoryGeoIpStore
            {
                Settings = Settings(GeoIpFailureMode.FailOpen, provider: GeoIpProviderNames.MaxMindWebService),
                Cache = new GeoIpCacheEntry(
                    "203.0.113.7",
                    GeoIpLookupStatus.Found.ToString(),
                    "US",
                    GeoIpProviderNames.MaxMindWebService,
                    "old",
                    Now.AddDays(-2),
                    Now.AddSeconds(-1))
            };
            var queue = new RecordingRefreshQueue();
            var useCase = new EvaluateGeoIpJoinUseCase(
                store,
                new GeoIpPolicyEvaluator(),
                queue,
                () => Now);

            var decision = useCase.Execute(new GeoIpJoinAttempt("203.0.113.7", null, false));

            Assert.True(decision.IsAllowed);
            Assert.False(decision.WasCacheHit);
            Assert.True(decision.RefreshEnqueued);
            var request = Assert.Single(queue.Requests);
            Assert.Equal(GeoIpProviderNames.MaxMindWebService, request.Provider);
            Assert.Equal("203.0.113.7", request.CanonicalIp);
        }

        [Fact]
        public void Refresh_worker_is_bounded_skips_disabled_and_persists_ttl_and_version()
        {
            var store = new MemoryGeoIpStore
            {
                Settings = Settings(GeoIpFailureMode.FailOpen, provider: GeoIpProviderNames.MaxMindWebService)
            };
            using var twoEntriesPersisted = new ManualResetEventSlim(false);
            store.CacheEntryCountChanged = count =>
            {
                if (count >= 2) twoEntriesPersisted.Set();
            };
            using var entered = new ManualResetEventSlim(false);
            var providerResult = new TaskCompletionSource<GeoIpLookupResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new DelegateGeoIpProvider(
                new GeoIpProviderMetadata(
                    GeoIpProviderNames.MaxMindWebService,
                    true,
                    "service-version-digest",
                    null),
                (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entered.Set();
                    return providerResult.Task;
                });
            using var worker = new GeoIpRefreshWorker(
                store,
                new[] { provider },
                capacity: 1,
                successTtl: TimeSpan.FromHours(6),
                failureTtl: TimeSpan.FromMinutes(2),
                drainTimeout: TimeSpan.FromSeconds(2),
                utcClock: () => Now);
            worker.Start();

            Assert.True(worker.TryWrite(Request("203.0.113.1")));
            Assert.True(entered.Wait(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
            Assert.True(worker.TryWrite(Request("203.0.113.2")));
            Assert.False(worker.TryWrite(Request("203.0.113.3")));
            providerResult.TrySetResult(GeoIpLookupResult.Found(
                "US",
                GeoIpProviderNames.MaxMindWebService,
                "service-version-digest"));
            Assert.True(twoEntriesPersisted.Wait(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));

            worker.Stop();
            Assert.All(store.CacheEntries, entry =>
            {
                Assert.Equal(Now.AddHours(6), entry.ExpiresAtUtc);
                Assert.Equal("service-version-digest", entry.SourceVersion);
            });
            Assert.Equal(1, worker.GetDiagnostics().RejectedCount);

            store.Settings = Settings(GeoIpFailureMode.FailOpen) with { IsEnabled = false };
            using var disabled = new GeoIpRefreshWorker(store, new[] { provider }, capacity: 1);
            disabled.Start();
            Assert.False(disabled.TryWrite(Request("203.0.113.4")));
            disabled.Stop();
        }

        [Fact]
        public async Task Providers_type_missing_database_and_credentials_as_unavailable_without_network()
        {
            using var local = new LocalMmdbGeoIpProvider(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "country.mmdb"));
            var localResult = await local.LookupAsync("203.0.113.10", CancellationToken.None);
            Assert.Equal(GeoIpLookupStatus.Unavailable, localResult.Status);
            Assert.Equal(GeoIpLookupFailure.File, localResult.Failure);

            var store = new MemoryGeoIpStore();
            using var web = new MaxMindWebServiceGeoIpProvider(store);
            var webResult = await web.LookupAsync("203.0.113.10", CancellationToken.None);
            Assert.Equal(GeoIpLookupStatus.Unavailable, webResult.Status);
            Assert.Equal(GeoIpLookupFailure.Credentials, webResult.Failure);
        }

        [Fact]
        public void MaxMind_credentials_are_owner_only_and_support_keep_replace_and_clear_without_echo()
        {
            var store = new MemoryGeoIpStore();
            var useCase = new UpdateGeoIpCredentialsUseCase(store, () => Now);
            var replacement = new GeoIpCredentialsUpdate(
                GeoIpSecretUpdate.Replace("12345"),
                GeoIpSecretUpdate.Replace("license-value-one"));

            Assert.Throws<GeoIpOwnerRequiredException>(() => useCase.Execute(
                new GeoIpCredentialsActor("admin-subject", isOwner: false),
                replacement));
            Assert.Empty(store.ListSecretMetadata());

            var replaced = useCase.Execute(
                new GeoIpCredentialsActor("owner-subject", isOwner: true),
                replacement);
            Assert.True(replaced.AccountId.IsSet);
            Assert.True(replaced.LicenseKey.IsSet);
            Assert.NotNull(replaced.AccountId.Fingerprint);
            Assert.NotNull(replaced.LicenseKey.Fingerprint);
            Assert.DoesNotContain(
                replaced.GetType().GetProperties(),
                property => string.Equals(property.Name, "SecretValue", StringComparison.Ordinal));

            var updated = useCase.Execute(
                new GeoIpCredentialsActor("owner-subject", isOwner: true),
                new GeoIpCredentialsUpdate(
                    GeoIpSecretUpdate.Keep(),
                    GeoIpSecretUpdate.Clear()));
            Assert.True(updated.AccountId.IsSet);
            Assert.False(updated.LicenseKey.IsSet);
            Assert.Equal("12345", store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.SecretValue);
            Assert.Null(store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey));
        }

        [Fact]
        public async Task MaxMind_provider_rotates_to_the_current_store_secrets_with_fake_transport_authentication()
        {
            var store = new MemoryGeoIpStore();
            var credentials = new UpdateGeoIpCredentialsUseCase(store, () => Now);
            var owner = new GeoIpCredentialsActor("owner-subject", isOwner: true);
            credentials.Execute(owner, new GeoIpCredentialsUpdate(
                GeoIpSecretUpdate.Replace("12345"),
                GeoIpSecretUpdate.Replace("license-value-one")));
            var probe = new MaxMindAuthenticationProbe("12345", "license-value-one");
            using var provider = new MaxMindWebServiceGeoIpProvider(store, probe.CreateHandler);

            var first = await provider.LookupAsync("203.0.113.10", CancellationToken.None);
            probe.Expect("67890", "license-value-two");
            credentials.Execute(owner, new GeoIpCredentialsUpdate(
                GeoIpSecretUpdate.Replace("67890"),
                GeoIpSecretUpdate.Replace("license-value-two")));
            var second = await provider.LookupAsync("203.0.113.11", CancellationToken.None);

            Assert.Equal(GeoIpLookupStatus.Found, first.Status);
            Assert.Equal(GeoIpLookupStatus.Found, second.Status);
            Assert.Equal(2, probe.AcceptedCount);
            Assert.Equal(0, probe.RejectedCount);
            Assert.DoesNotContain("license-value", first.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("license-value", second.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task MaxMind_timeout_is_failure_cached_and_drives_fail_open_or_fail_closed_without_requery()
        {
            var store = new MemoryGeoIpStore
            {
                Settings = Settings(
                    GeoIpFailureMode.FailOpen,
                    provider: GeoIpProviderNames.MaxMindWebService)
            };
            SetMaxMindCredentials(store);
            var transportCalls = 0;
            using var cachePersisted = new ManualResetEventSlim(false);
            store.CacheEntryCountChanged = _ => cachePersisted.Set();
            using var provider = new MaxMindWebServiceGeoIpProvider(
                store,
                () => new DelegateHttpMessageHandler((_, _) =>
                {
                    Interlocked.Increment(ref transportCalls);
                    return Task.FromException<HttpResponseMessage>(
                        new TaskCanceledException("transport-timeout-secret"));
                }));
            using var worker = new GeoIpRefreshWorker(
                store,
                new[] { provider },
                failureTtl: TimeSpan.FromMinutes(2),
                drainTimeout: TimeSpan.FromSeconds(2),
                utcClock: () => Now);
            worker.Start();

            Assert.True(worker.TryWrite(Request("203.0.113.50")));
            Assert.True(cachePersisted.Wait(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
            worker.Stop();

            var cached = Assert.Single(store.CacheEntries);
            Assert.Equal(GeoIpLookupStatus.Unavailable.ToString(), cached.LookupStatus);
            Assert.Equal(Now.AddMinutes(2), cached.ExpiresAtUtc);
            var refreshQueue = new RecordingRefreshQueue();
            var useCase = new EvaluateGeoIpJoinUseCase(
                store,
                new GeoIpPolicyEvaluator(),
                refreshQueue,
                () => Now);

            var failOpen = useCase.Execute(
                new GeoIpJoinAttempt("203.0.113.50", null, false));
            store.Settings = store.Settings! with { FailureMode = GeoIpFailureMode.FailClosed };
            var failClosed = useCase.Execute(
                new GeoIpJoinAttempt("203.0.113.50", null, false));

            Assert.True(failOpen.IsAllowed);
            Assert.False(failClosed.IsAllowed);
            Assert.True(failOpen.WasCacheHit);
            Assert.True(failClosed.WasCacheHit);
            Assert.Empty(refreshQueue.Requests);
            Assert.Equal(1, Volatile.Read(ref transportCalls));
            Assert.DoesNotContain("transport-timeout-secret", cached.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task MaxMind_http_rejection_is_typed_as_transport_unavailable()
        {
            var store = new MemoryGeoIpStore();
            SetMaxMindCredentials(store);
            using var provider = new MaxMindWebServiceGeoIpProvider(
                store,
                () => new DelegateHttpMessageHandler((_, _) => Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent(
                            "{\"code\":\"SERVER_ERROR\",\"error\":\"response-secret\"}",
                            Encoding.UTF8,
                            "application/json")
                    })));

            var result = await provider.LookupAsync(
                "203.0.113.51",
                TestContext.Current.CancellationToken);

            Assert.Equal(GeoIpLookupStatus.Unavailable, result.Status);
            Assert.Equal(GeoIpLookupFailure.Http, result.Failure);
            Assert.DoesNotContain("response-secret", result.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void SevenDays_join_callback_returns_and_rejects_before_external_lookup_completes()
        {
            var store = new MemoryGeoIpStore
            {
                Settings = Settings(
                    GeoIpFailureMode.FailClosed,
                    provider: GeoIpProviderNames.MaxMindWebService,
                    rejectionMessage: "此服务器不允许当前连接")
            };
            using var providerEntered = new ManualResetEventSlim(false);
            var providerResult = new TaskCompletionSource<GeoIpLookupResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new DelegateGeoIpProvider(
                new GeoIpProviderMetadata(
                    GeoIpProviderNames.MaxMindWebService,
                    true,
                    null,
                    null),
                (_, _) =>
                {
                    providerEntered.Set();
                    return providerResult.Task;
                });
            using var worker = new GeoIpRefreshWorker(store, new[] { provider }, capacity: 4);
            worker.Start();
            var useCase = new EvaluateGeoIpJoinUseCase(
                store,
                new GeoIpPolicyEvaluator(),
                worker,
                () => Now);
            Action<SevenDaysGeoIpJoinSnapshot>? joined = null;
            string? rejection = null;
            using var runtime = new SevenDaysGeoIpJoinPolicyRuntime(
                useCase,
                handler =>
                {
                    joined = handler;
                    return new CallbackDisposable(() => joined = null);
                },
                (_, message) => rejection = message);
            runtime.Start();

            joined!(new SevenDaysGeoIpJoinSnapshot(
                new object(),
                "203.0.113.99",
                "EOS_99",
                IsConfirmedNativeAdministrator: false));

            Assert.Equal("此服务器不允许当前连接", rejection);
            Assert.False(providerResult.Task.IsCompleted);
            Assert.True(providerEntered.Wait(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
            providerResult.TrySetResult(GeoIpLookupResult.Found(
                "US",
                GeoIpProviderNames.MaxMindWebService,
                null));
            worker.Stop();
        }

        [Fact]
        public void Host_options_normalize_only_the_server_owned_database_path()
        {
            var relative = Path.Combine("test-data", "GeoLite2-Country.mmdb");

            var options = PanelHostOptions.FromBinding(
                18080,
                "127.0.0.1",
                "http",
                geoIpDatabasePath: relative);

            Assert.Equal(Path.GetFullPath(relative), options.GeoIpDatabasePath);
            Assert.DoesNotContain("secret", options.GeoIpDatabasePath, StringComparison.OrdinalIgnoreCase);
        }

        private static GeoIpAccessPolicySettings Settings(
            GeoIpFailureMode failureMode,
            bool bypassAdmins = true,
            string provider = GeoIpProviderNames.LocalMmdb,
            string rejectionMessage = "Connection denied by server policy.") =>
            new GeoIpAccessPolicySettings(
                7,
                true,
                provider,
                failureMode,
                bypassAdmins,
                rejectionMessage);

        private static GeoIpRefreshRequest Request(string ip) =>
            new GeoIpRefreshRequest(
                GeoIpProviderNames.MaxMindWebService,
                ip,
                7,
                Now);

        private static void SetMaxMindCredentials(MemoryGeoIpStore store)
        {
            var credentials = new UpdateGeoIpCredentialsUseCase(store, () => Now);
            credentials.Execute(
                new GeoIpCredentialsActor("owner-subject", isOwner: true),
                new GeoIpCredentialsUpdate(
                    GeoIpSecretUpdate.Replace("12345"),
                    GeoIpSecretUpdate.Replace("license-value")));
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingRefreshQueue : IGeoIpRefreshQueue
        {
            public List<GeoIpRefreshRequest> Requests { get; } = new List<GeoIpRefreshRequest>();

            public bool TryWrite(GeoIpRefreshRequest request)
            {
                Requests.Add(request);
                return true;
            }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class StaticRefreshDiagnostics : IGeoIpRefreshDiagnostics
        {
            private readonly GeoIpRefreshDiagnostics diagnostics;

            public StaticRefreshDiagnostics(GeoIpRefreshDiagnostics diagnostics) =>
                this.diagnostics = diagnostics;

            public GeoIpRefreshDiagnostics GetDiagnostics() => diagnostics;
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class DelegateGeoIpProvider : IGeoIpProvider
        {
            private readonly Func<string, CancellationToken, Task<GeoIpLookupResult>> lookup;

            public DelegateGeoIpProvider(
                GeoIpProviderMetadata metadata,
                Func<string, CancellationToken, Task<GeoIpLookupResult>> lookup)
            {
                Metadata = metadata;
                this.lookup = lookup;
            }

            public GeoIpProviderMetadata Metadata { get; }

            public Task<GeoIpLookupResult> LookupAsync(
                string canonicalIp,
                CancellationToken cancellationToken) =>
                lookup(canonicalIp, cancellationToken);

            public void Dispose()
            {
            }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? callback;

            public CallbackDisposable(Action callback) => this.callback = callback;

            public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class MemoryGeoIpStore : IGeoIpAccessPolicyStore
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, GeoIpSecretValue> secrets =
                new Dictionary<string, GeoIpSecretValue>(StringComparer.Ordinal);

            public GeoIpAccessPolicySettings? Settings { get; set; }
            public GeoIpCacheEntry? Cache { get; set; }
            public List<GeoIpCacheEntry> CacheEntries { get; } = new List<GeoIpCacheEntry>();
            public Action<int>? CacheEntryCountChanged { get; set; }
            public List<GeoIpDecision> Decisions { get; } = new List<GeoIpDecision>();
            public IReadOnlyList<GeoIpNetworkRule> NetworkRules { get; set; } =
                Array.Empty<GeoIpNetworkRule>();
            public IReadOnlyList<GeoIpCountryRule> CountryRules { get; set; } =
                Array.Empty<GeoIpCountryRule>();

            public GeoIpAccessPolicySettings? GetSettings()
            {
                lock (sync) return Settings;
            }

            public void SaveSettings(GeoIpAccessPolicySettings settings, long expectedVersion) =>
                Settings = settings;

            public void SetSecret(GeoIpSecretValue secret)
            {
                lock (sync) secrets[secret.SecretKey] = secret;
            }

            public void ApplySecretChanges(IReadOnlyList<GeoIpSecretMutation> changes)
            {
                lock (sync)
                {
                    var next = new Dictionary<string, GeoIpSecretValue>(secrets, StringComparer.Ordinal);
                    foreach (var change in changes)
                    {
                        if (change.Replacement == null) next.Remove(change.SecretKey);
                        else next[change.SecretKey] = change.Replacement;
                    }
                    secrets.Clear();
                    foreach (var pair in next) secrets.Add(pair.Key, pair.Value);
                }
            }

            public GeoIpSecretValue? GetSecret(string secretKey)
            {
                lock (sync) return secrets.TryGetValue(secretKey, out var value) ? value : null;
            }

            public IReadOnlyList<GeoIpSecretMetadata> ListSecretMetadata()
            {
                lock (sync)
                {
                    return secrets.Values
                        .Select(value => new GeoIpSecretMetadata(
                            value.SecretKey,
                            value.Fingerprint,
                            value.UpdatedAtUtc))
                        .ToArray();
                }
            }

            public void ReplaceNetworkRules(IReadOnlyList<GeoIpNetworkRule> rules) =>
                NetworkRules = rules.ToArray();

            public IReadOnlyList<GeoIpNetworkRule> ListNetworkRules() => NetworkRules;

            public void ReplaceCountryRules(IReadOnlyList<GeoIpCountryRule> rules) =>
                CountryRules = rules.ToArray();

            public IReadOnlyList<GeoIpCountryRule> ListCountryRules() => CountryRules;

            public void UpsertCache(GeoIpCacheEntry entry)
            {
                int count;
                lock (sync)
                {
                    Cache = entry;
                    CacheEntries.Add(entry);
                    count = CacheEntries.Count;
                }
                CacheEntryCountChanged?.Invoke(count);
            }

            public GeoIpCacheEntry? FindCache(string ipAddress)
            {
                lock (sync)
                {
                    if (Cache == null) return null;
                    return string.Equals(
                        Cache.CanonicalIp,
                        ipAddress,
                        StringComparison.OrdinalIgnoreCase)
                        ? Cache
                        : null;
                }
            }

            public void RecordDecision(GeoIpDecision decision)
            {
                lock (sync) Decisions.Add(decision);
            }

            public GeoIpDecisionPage QueryDecisions(GeoIpDecisionQuery query)
            {
                lock (sync)
                    return new GeoIpDecisionPage(Decisions.Take(query.PageSize).ToArray(), null);
            }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class MaxMindAuthenticationProbe
        {
            private string expectedAccountId;
            private string expectedLicenseKey;

            public MaxMindAuthenticationProbe(string accountId, string licenseKey)
            {
                expectedAccountId = accountId;
                expectedLicenseKey = licenseKey;
            }

            public int AcceptedCount { get; private set; }
            public int RejectedCount { get; private set; }

            public void Expect(string accountId, string licenseKey)
            {
                expectedAccountId = accountId;
                expectedLicenseKey = licenseKey;
            }

            public HttpMessageHandler CreateHandler() => new MaxMindAuthenticationHandler(this);

            private bool Accept(HttpRequestMessage request)
            {
                var authorization = request.Headers.Authorization;
                if (!string.Equals(authorization?.Scheme, "Basic", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(authorization.Parameter))
                {
                    RejectedCount++;
                    return false;
                }

                string decoded;
                try
                {
                    decoded = Encoding.UTF8.GetString(
                        Convert.FromBase64String(authorization.Parameter!));
                }
                catch (FormatException)
                {
                    RejectedCount++;
                    return false;
                }
                var expected = int.Parse(expectedAccountId, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture) + ":" + expectedLicenseKey;
                if (!string.Equals(decoded, expected, StringComparison.Ordinal))
                {
                    RejectedCount++;
                    return false;
                }

                AcceptedCount++;
                return true;
            }

            [Trait("Capability", "Administration")]

            [Trait("Boundary", "Application")]

            private sealed class MaxMindAuthenticationHandler : HttpMessageHandler
            {
                private readonly MaxMindAuthenticationProbe owner;

                public MaxMindAuthenticationHandler(MaxMindAuthenticationProbe owner) =>
                    this.owner = owner;

                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!owner.Accept(request))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                        {
                            Content = new StringContent(
                                "{\"code\":\"AUTHORIZATION_INVALID\",\"error\":\"unauthorized\"}",
                                Encoding.UTF8,
                                "application/json")
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"country\":{\"iso_code\":\"US\"}}",
                            Encoding.UTF8,
                            "application/json")
                    });
                }
            }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class DelegateHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;

            public DelegateHttpMessageHandler(
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
                this.send = send;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                send(request, cancellationToken);
        }
    }
}
